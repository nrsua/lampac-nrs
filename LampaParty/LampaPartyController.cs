using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using System;
using System.IO;
using System.Linq;

namespace LampaParty
{
    [Route("lampaparty")]
    public class LampaPartyController : BaseController
    {
        const int MAX_ROOMS_TOTAL = 500;
        const int MAX_ROOMS_PER_OWNER = 3;

        [AllowAnonymous]
        [HttpGet]
        [Route("/lampaparty.js")]
        public ActionResult GetPlugin()
        {
            string pluginPath = Path.Combine(ModInit.modpath, "plugin.js");
            long mtime = System.IO.File.GetLastWriteTimeUtc(pluginPath).Ticks;
            string memKey = "lampaparty:plugin.js:" + mtime;

            if (!memoryCache.TryGetValue(memKey, out string rawJs))
            {
                rawJs = System.IO.File.ReadAllText(pluginPath);
                memoryCache.Set(memKey, rawJs, TimeSpan.FromMinutes(10));
            }

            string js = rawJs.Replace("{localhost}", this.host);
            return Content(js, "application/javascript; charset=utf-8");
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("/lampaparty/list")]
        public ActionResult ListRooms()
        {
            var rooms = RoomDb.Rooms.Values
                .OrderByDescending(r => r.update_time)
                .Select(r => new
                {
                    id = r.id,
                    name = r.name,
                    title = r.title,
                    poster = r.poster,
                    owner = r.owner_name,
                    has_password = r.has_password,
                    members = RoomDb.Members.Values.Count(m => m.room_id == r.id),
                    state = r.state,
                    tmdb_id = r.tmdb_id,
                    source = r.source,
                    type = r.type,
                    has_stream = !string.IsNullOrEmpty(r.stream_url),
                    create_time = r.create_time
                })
                .ToList();

            return new JsonResult(new { rooms });
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("/lampaparty/create")]
        public ActionResult CreateRoom(
            [FromForm] string name,
            [FromForm] string stream_url,
            [FromForm] string title,
            [FromForm] string poster,
            [FromForm] string password,
            [FromForm] string owner_uid,
            [FromForm] string owner_name,
            [FromForm] int tmdb_id,
            [FromForm] string source,
            [FromForm] string type,
            [FromForm] string initial_state,
            [FromForm] double initial_position)
        {
            bool hasStream = !string.IsNullOrWhiteSpace(stream_url);
            bool hasTmdb = tmdb_id > 0;

            if (!hasStream && !hasTmdb)
                return BadRequest(new { error = "stream_url or tmdb_id required" });

            if (RoomDb.Rooms.Count >= MAX_ROOMS_TOTAL)
                return StatusCode(429, new { error = "too many rooms on server" });

            if (!string.IsNullOrEmpty(owner_uid))
            {
                int ownerRooms = RoomDb.Rooms.Values.Count(r => string.Equals(r.owner_uid, owner_uid, StringComparison.Ordinal));
                if (ownerRooms >= MAX_ROOMS_PER_OWNER)
                    return StatusCode(429, new { error = "too many rooms per owner" });
            }

            string id = GenerateUniqueRoomId();
            if (id == null)
                return StatusCode(503, new { error = "could not allocate room id" });

            string startState = (initial_state == "playing" || initial_state == "paused") ? initial_state : "paused";

            var room = new RoomModel
            {
                id = id,
                name = string.IsNullOrWhiteSpace(name) ? ("Room-" + id) : name.Trim(),
                stream_url = hasStream ? stream_url.Trim() : string.Empty,
                title = title ?? string.Empty,
                poster = poster ?? string.Empty,
                owner_uid = owner_uid ?? string.Empty,
                owner_name = string.IsNullOrWhiteSpace(owner_name) ? "Host" : owner_name.Trim(),
                password_hash = RoomDb.HashPassword(password),
                tmdb_id = tmdb_id,
                source = string.IsNullOrWhiteSpace(source) ? "tmdb" : source.Trim(),
                type = string.IsNullOrWhiteSpace(type) ? "movie" : type.Trim(),
                state = startState,
                position = initial_position > 0 ? initial_position : 0,
                at_server_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                create_time = DateTime.UtcNow,
                update_time = DateTime.UtcNow
            };

            if (!RoomDb.Rooms.TryAdd(id, room))
                return StatusCode(503, new { error = "could not allocate room id" });

            return new JsonResult(new { id = id, name = room.name, has_password = room.has_password });
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("/lampaparty/join")]
        public ActionResult JoinRoom([FromQuery] string id, [FromQuery] string password)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { error = "id required" });

            if (!RoomDb.Rooms.TryGetValue(id, out var room))
                return NotFound(new { error = "Room not found" });

            if (!RoomDb.VerifyPassword(room, password))
                return Unauthorized(new { error = "Wrong password", has_password = true });

            return new JsonResult(new
            {
                id = room.id,
                name = room.name,
                stream_url = room.stream_url,
                title = room.title,
                poster = room.poster,
                tmdb_id = room.tmdb_id,
                source = room.source,
                type = room.type,
                state = room.state,
                position = room.position,
                at_server_time = room.at_server_time,
                owner = room.owner_name,
                owner_uid = room.owner_uid,
                members = RoomDb.Members.Values.Count(m => m.room_id == id)
            });
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("/lampaparty/info")]
        public ActionResult GetRoomInfo([FromQuery] string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { error = "id required" });

            if (!RoomDb.Rooms.TryGetValue(id, out var room))
                return NotFound(new { error = "Room not found" });

            return new JsonResult(new
            {
                id = room.id,
                name = room.name,
                title = room.title,
                poster = room.poster,
                owner = room.owner_name,
                has_password = room.has_password,
                tmdb_id = room.tmdb_id,
                source = room.source,
                type = room.type,
                state = room.state,
                position = room.position,
                at_server_time = room.at_server_time,
                members = RoomDb.Members.Values.Count(m => m.room_id == id)
            });
        }

        private static string GenerateUniqueRoomId()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var rnd = Random.Shared;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                int len = attempt < 16 ? 6 : 8;
                var buf = new char[len];
                for (int i = 0; i < len; i++) buf[i] = chars[rnd.Next(chars.Length)];
                string id = new string(buf);
                if (!RoomDb.Rooms.ContainsKey(id)) return id;
            }
            return null;
        }
    }
}
