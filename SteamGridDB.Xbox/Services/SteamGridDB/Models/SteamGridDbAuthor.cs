using System.Runtime.Serialization;

namespace SteamGridDB.Xbox.Services.SteamGridDB.Models
{
    /// <summary>
    /// Represents an author/uploader on SteamGridDB.
    /// </summary>
    [DataContract]
    public class SteamGridDbAuthor
    {
        [DataMember(Name = "name")]
        public string Name
        {
            get; set;
        }

        [DataMember(Name = "steam64")]
        public string Steam64
        {
            get; set;
        }

        [DataMember(Name = "avatar")]
        public string Avatar
        {
            get; set;
        }
    }
}
