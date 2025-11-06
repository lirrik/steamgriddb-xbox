using System.Runtime.Serialization;

namespace SteamGridDB.Xbox.Services.SteamGridDB.Models
{
    /// <summary>
    /// Represents a game search result from SteamGridDB.
    /// </summary>
    [DataContract]
    public class SteamGridDbGame
    {
        [DataMember(Name = "id")]
        public int Id
        {
            get; set;
        }

        [DataMember(Name = "name")]
        public string Name
        {
            get; set;
        }

        [DataMember(Name = "types")]
        public string[] Types
        {
            get; set;
        }

        [DataMember(Name = "verified")]
        public bool Verified
        {
            get; set;
        }
    }
}
