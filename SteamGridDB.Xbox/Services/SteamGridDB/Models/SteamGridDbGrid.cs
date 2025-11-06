using System.Runtime.Serialization;

namespace SteamGridDB.Xbox.Services.SteamGridDB.Models
{
    /// <summary>
    /// Represents grid art/image from SteamGridDB.
    /// </summary>
    [DataContract]
    public class SteamGridDbGrid
    {
        [DataMember(Name = "id")]
        public int Id
        {
            get; set;
        }

        [DataMember(Name = "score")]
        public int Score
        {
            get; set;
        }

        [DataMember(Name = "style")]
        public string Style
        {
            get; set;
        }

        [DataMember(Name = "url")]
        public string Url
        {
            get; set;
        }

        [DataMember(Name = "thumb")]
        public string Thumb
        {
            get; set;
        }

        [DataMember(Name = "tags")]
        public string[] Tags
        {
            get; set;
        }

        [DataMember(Name = "language")]
        public string Language
        {
            get; set;
        }

        [DataMember(Name = "notes")]
        public string Notes
        {
            get; set;
        }

        [DataMember(Name = "author")]
        public SteamGridDbAuthor Author
        {
            get; set;
        }
    }
}
