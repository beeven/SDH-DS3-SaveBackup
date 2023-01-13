public class DS3SaveBackupOptions
{
    public string WorkingDirectory { get; set; } = "/home/deck/.local/share/DS3SaveBackup/";

    public string ClientId { get; set; } = "7e9bf271-a6cd-4786-b4f6-7980ff10acf8";
    public string[] Scopes { get; set; } = { "User.Read", "Files.ReadWrite" };
    public string Socket { get; set; } = "/tmp/ds3-savebackup.sock";
    public string CloudFolder { get; set; } = "DS3SaveBackup";
    public string LocalFolder { get; set; } = "/home/deck/.local/share/Steam/steamapps/compatdata/374320/pfx/drive_c/users/steamuser/AppData/Roaming/DarkSoulsIII/0110000100f9e486";
}