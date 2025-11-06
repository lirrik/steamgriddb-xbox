namespace SteamGridDB.Xbox.Models
{
    public enum GamePlatform
    {
        Steam,
        GOG,
        Epic,
        Ubisoft,
        BattleNet,
        EA,
        Unknown
    }

    public class GamePlatformHelper
    {
        public static GamePlatform FromXboxDirectory(string platformString)
        {
            switch (platformString.ToLower())
            {
                case "steam":
                    return GamePlatform.Steam;
                case "gog":
                    return GamePlatform.GOG;
                case "epic":
                    return GamePlatform.Epic;
                case "ubi":
                    return GamePlatform.Ubisoft;
                case "bnet":
                    return GamePlatform.BattleNet;
                case "ea":
                    return GamePlatform.EA;
                default:
                    return GamePlatform.Unknown;
            }
        }

        public static string GamePlatformToSGDBApiString(GamePlatform platform)
        {
            switch (platform)
            {
                case GamePlatform.Steam:
                    return "steam";
                case GamePlatform.GOG:
                    return "gog";
                case GamePlatform.Epic:
                    return "egs";
                case GamePlatform.Ubisoft:
                    return "uplay";
                case GamePlatform.BattleNet:
                    return "bnet";
                case GamePlatform.EA:
                    return "origin";
                default:
                    return null;
            }
        }
    }
}