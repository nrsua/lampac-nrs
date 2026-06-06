using Shared;
using Shared.Models.Events;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LampaParty
{
    public static class WsEvents
    {
        static int initialized = 0;
        static Timer heartbeatTimer;

        static readonly ConcurrentDictionary<string, (string roomId, string uid, string displayName)> eventClients = new();

        public static bool IsConnectionActive(string connectionId) =>
            !string.IsNullOrEmpty(connectionId) && eventClients.ContainsKey(connectionId);

        static long ServerNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static void Start()
        {
            if (System.Threading.Interlocked.Exchange(ref initialized, 1) == 1)
                return;

            EventListener.NwsMessage += OnNwsMessage;
            EventListener.NwsDisconnected += OnNwsDisconnected;
            GcTask.Start();

            heartbeatTimer = new Timer(SendHeartbeats, null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
        }

        public static void Stop()
        {
            EventListener.NwsMessage -= OnNwsMessage;
            EventListener.NwsDisconnected -= OnNwsDisconnected;
            GcTask.Stop();
            heartbeatTimer?.Dispose();
            heartbeatTimer = null;
            System.Threading.Interlocked.Exchange(ref initialized, 0);
        }

        static void SendHeartbeats(object state)
        {
            try
            {
                long now = ServerNowMs();
                var connectionIds = eventClients.Keys.ToArray();
                foreach (var cid in connectionIds)
                {
                    try
                    {
                        _ = Startup.Nws.SendAsync(cid, "lampaparty_server_ping", now);
                    }
                    catch { }
                }
            }
            catch { }
        }

        static void OnNwsDisconnected(EventNwsDisconnected e)
        {
            if (string.IsNullOrEmpty(e.connectionId)) return;

            if (eventClients.TryRemove(e.connectionId, out var info))
            {
                _ = LeaveAsync(e.connectionId, info.roomId, info.uid, info.displayName, broadcastLeft: true);
            }
        }

        static void OnNwsMessage(EventNwsMessage e)
        {
            if (string.IsNullOrEmpty(e.method)) return;

            string method = e.method.ToLowerInvariant();
            if (!method.StartsWith("lampaparty_")) return;

            if (method == "lampaparty_join")
            {
                string roomId = GetStringArg(e.args, 0);
                string uid = GetStringArg(e.args, 1);
                string baseName = GetStringArg(e.args, 2);

                if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(uid))
                    _ = JoinAsync(e.connectionId, roomId, uid, baseName);
                return;
            }

            if (method == "lampaparty_ping")
            {
                long t0 = GetLongArg(e.args, 0);
                _ = Startup.Nws.SendAsync(e.connectionId, "lampaparty_pong", t0, ServerNowMs());
                return;
            }

            string rId = GetStringArg(e.args, 0);
            string usrId = GetStringArg(e.args, 1);
            if (string.IsNullOrEmpty(rId) || string.IsNullOrEmpty(usrId)) return;

            if (method == "lampaparty_sync" || method == "lampaparty_action")
            {
                if (!eventClients.TryGetValue(e.connectionId, out var senderInfo)) return;
                if (!string.Equals(senderInfo.roomId, rId, StringComparison.Ordinal)) return;
                if (!string.Equals(senderInfo.uid, usrId, StringComparison.Ordinal)) return;

                string state = GetStringArg(e.args, 2);
                if (state != "playing" && state != "paused") return;
                double position = GetDoubleArg(e.args, 3);
                if (double.IsNaN(position) || double.IsInfinity(position) || position < 0 || position > 2592000) return;
                bool isAction = method == "lampaparty_action";
                _ = HandleSyncAsync(senderInfo.roomId, e.connectionId, state, position, broadcastNotice: isAction);
            }
            else if (method == "lampaparty_url_change")
            {
                if (!eventClients.TryGetValue(e.connectionId, out var senderInfo)) return;
                if (!string.Equals(senderInfo.roomId, rId, StringComparison.Ordinal)) return;
                if (!string.Equals(senderInfo.uid, usrId, StringComparison.Ordinal)) return;
                if (!RoomDb.Rooms.TryGetValue(senderInfo.roomId, out var room)) return;
                if (!string.Equals(room.owner_uid, usrId, StringComparison.Ordinal)) return;

                string newUrl = GetStringArg(e.args, 2);
                if (string.IsNullOrWhiteSpace(newUrl) || newUrl.Length > 8000) return;
                string newTitle = GetStringArg(e.args, 3) ?? string.Empty;
                if (newTitle.Length > 500) return;

                room.stream_url = newUrl.Trim();
                if (!string.IsNullOrEmpty(newTitle)) room.title = newTitle.Trim();
                room.state = "paused";
                room.position = 0;
                room.at_server_time = ServerNowMs();
                room.update_time = DateTime.UtcNow;

                _ = BroadcastUrlChangeAsync(senderInfo.roomId, e.connectionId, room.stream_url, room.title);
            }
            else if (method == "lampaparty_leave")
            {
                if (eventClients.TryRemove(e.connectionId, out var info))
                    _ = LeaveAsync(e.connectionId, info.roomId, info.uid, info.displayName, broadcastLeft: true);
            }
        }

        static async Task JoinAsync(string connectionId, string roomId, string uid, string baseName)
        {
            var oldConnections = eventClients.Where(x => x.Value.uid == uid && x.Key != connectionId).ToList();
            foreach (var old in oldConnections)
            {
                _ = Startup.Nws.SendAsync(old.Key, "lampaparty_kicked");
                if (eventClients.TryRemove(old.Key, out var info))
                    await LeaveAsync(old.Key, info.roomId, info.uid, info.displayName, broadcastLeft: false);
            }

            if (!RoomDb.Rooms.TryGetValue(roomId, out var room))
            {
                _ = Startup.Nws.SendAsync(connectionId, "lampaparty_error", "room_not_found");
                return;
            }

            string displayName = RoomDb.AssignDisplayName(roomId, baseName);

            eventClients.AddOrUpdate(connectionId, (roomId, uid, displayName), (_, __) => (roomId, uid, displayName));

            var member = new RoomMemberModel
            {
                room_id = roomId,
                uid = uid,
                connection_id = connectionId,
                base_name = baseName,
                display_name = displayName,
                last_seen = DateTime.UtcNow
            };
            RoomDb.Members.AddOrUpdate(connectionId, member, (_, __) => member);

            long at = room.at_server_time > 0 ? room.at_server_time : ServerNowMs();
            _ = Startup.Nws.SendAsync(connectionId, "lampaparty_joined", displayName, room.state, room.position, at);
            _ = Startup.Nws.SendAsync(connectionId, "lampaparty_sync_update", room.state, room.position, at);

            await BroadcastMembersAsync(roomId);
            await BroadcastNoticeAsync(roomId, connectionId, "joined", displayName);
        }

        static async Task LeaveAsync(string connectionId, string roomId, string uid, string displayName, bool broadcastLeft)
        {
            RoomDb.Members.TryRemove(connectionId, out _);

            if (!RoomDb.Rooms.TryGetValue(roomId, out var room))
                return;

            bool wasHost = !string.IsNullOrEmpty(room.owner_uid) &&
                           !string.IsNullOrEmpty(uid) &&
                           string.Equals(room.owner_uid, uid, StringComparison.Ordinal);

            if (wasHost)
            {
                var remaining = eventClients.Where(x => x.Value.roomId == roomId && x.Key != connectionId).ToArray();
                if (remaining.Length == 0)
                {
                    RoomDb.Rooms.TryRemove(roomId, out _);
                    return;
                }

                var newHost = remaining[0];
                room.owner_uid = newHost.Value.uid;
                room.owner_name = newHost.Value.displayName;
                room.update_time = DateTime.UtcNow;

                if (broadcastLeft && !string.IsNullOrEmpty(displayName))
                    await BroadcastNoticeAsync(roomId, connectionId, "left", displayName);

                var notifyTargets = remaining.Select(r => r.Key).ToArray();
                var hostTasks = notifyTargets.Select(t => Startup.Nws.SendAsync(t, "lampaparty_host_changed", newHost.Value.uid, newHost.Value.displayName));
                await Task.WhenAll(hostTasks);

                var noticeTasks = notifyTargets.Select(t => Startup.Nws.SendAsync(t, "lampaparty_notice", "host_changed", newHost.Value.displayName));
                await Task.WhenAll(noticeTasks);

                await BroadcastMembersAsync(roomId);
                return;
            }

            bool hasMembers = RoomDb.Members.Values.Any(m => m.room_id == roomId);
            if (!hasMembers)
            {
                RoomDb.Rooms.TryRemove(roomId, out _);
            }
            else
            {
                if (broadcastLeft && !string.IsNullOrEmpty(displayName))
                    await BroadcastNoticeAsync(roomId, connectionId, "left", displayName);
                await BroadcastMembersAsync(roomId);
            }
        }

        static async Task HandleSyncAsync(string roomId, string senderConnectionId, string state, double position, bool broadcastNotice)
        {
            long atServer = ServerNowMs();

            if (RoomDb.Rooms.TryGetValue(roomId, out var room))
            {
                room.state = state;
                room.position = position;
                room.at_server_time = atServer;
                room.update_time = DateTime.UtcNow;
            }

            var targets = eventClients.Where(i => i.Value.roomId == roomId && i.Key != senderConnectionId).Select(i => i.Key).ToArray();
            if (targets.Length > 0)
            {
                var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "lampaparty_sync_update", state, position, atServer));
                await Task.WhenAll(tasks);
            }

            if (broadcastNotice && eventClients.TryGetValue(senderConnectionId, out var who))
            {
                string verb = state == "paused" ? "paused" : (state == "playing" ? "resumed" : "seeked");
                await BroadcastNoticeAsync(roomId, senderConnectionId, verb, who.displayName);
            }
        }

        static async Task BroadcastUrlChangeAsync(string roomId, string excludeConnectionId, string url, string title)
        {
            var targets = eventClients
                .Where(i => i.Value.roomId == roomId && i.Key != excludeConnectionId)
                .Select(i => i.Key).ToArray();
            if (targets.Length == 0) return;

            var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "lampaparty_url_change", url, title));
            await Task.WhenAll(tasks);
        }

        static async Task BroadcastNoticeAsync(string roomId, string excludeConnectionId, string verb, string displayName)
        {
            var targets = eventClients
                .Where(i => i.Value.roomId == roomId && i.Key != excludeConnectionId)
                .Select(i => i.Key).ToArray();
            if (targets.Length == 0) return;

            var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "lampaparty_notice", verb, displayName));
            await Task.WhenAll(tasks);
        }

        static async Task BroadcastMembersAsync(string roomId)
        {
            var inRoom = eventClients.Where(i => i.Value.roomId == roomId).ToArray();
            var targets = inRoom.Select(i => i.Key).ToArray();
            if (targets.Length == 0) return;

            var names = inRoom.Select(i => i.Value.displayName).ToArray();
            var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "lampaparty_members", targets.Length, names));
            await Task.WhenAll(tasks);
        }

        static string GetStringArg(JsonElement args, int index)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index) return null;
            var element = args[index];
            if (element.ValueKind == JsonValueKind.String) return element.GetString();
            if (element.ValueKind == JsonValueKind.Null) return null;
            return element.ToString();
        }

        static double GetDoubleArg(JsonElement args, int index)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index) return 0;
            var element = args[index];
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double val)) return val;
            return 0;
        }

        static long GetLongArg(JsonElement args, int index)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index) return 0;
            var element = args[index];
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long val)) return val;
            return 0;
        }
    }
}
