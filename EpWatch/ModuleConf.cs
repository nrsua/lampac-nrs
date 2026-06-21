namespace EpWatch;

public class ModuleConf
{
    public bool enable { get; set; } = true;

    public string bot_token { get; set; } = "";

    public string lampac_host { get; set; } = "";

    public string balancer_uid { get; set; } = "";

    public string tmdb_api_key { get; set; } = "4ef0d7355d9ffb5151e987764708ce96";

    public string tmdb_lang { get; set; } = "uk-UA";

    public int check_interval_minutes { get; set; } = 60;

    public int initial_delay_minutes { get; set; } = 2;

    public int balancer_timeout_seconds { get; set; } = 10;

    public int balancer_throttle_ms { get; set; } = 400;

    public string[] allowed_balancers { get; set; } = null;

    public bool tvdb_enable { get; set; } = true;

    public string tvdb_host { get; set; } = "https://skyhook.sonarr.tv/v1/tvdb";

    public string id_format { get; set; } = "{type}|{id}";

    public bool push_open_button { get; set; } = false;

    public bool debug { get; set; } = false;
}