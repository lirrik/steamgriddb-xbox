using System.Runtime.Serialization;

namespace SteamGridDB.Xbox.Services.SteamGridDB.Models
{
    /// <summary>
    /// API response wrapper.
    /// </summary>
    /// <typeparam name="T">Type of data in the response.</typeparam>
    [DataContract]
    public class SteamGridDbResponse<T>
    {
        [DataMember(Name = "success")]
        public bool Success
        {
            get; set;
        }

        [DataMember(Name = "data")]
        public T Data
        {
            get; set;
        }

        [DataMember(Name = "errors")]
        public string[] Errors
        {
            get; set;
        }
    }
}
