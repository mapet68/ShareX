#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (C) 2008-2013 ShareX Developers

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
using System.Net;
using Newtonsoft.Json.Linq;
using System.Threading;
using Newtonsoft.Json;

#endregion License Information (GPL v3)

using System;
using System.IO;
using System.Security.Cryptography;

namespace UploadersLib.ImageUploaders
{
    public class MediaCrushUploader : ImageUploader
    {
        public MediaCrushUploader()
        {
        }

        public override UploadResult Upload(Stream stream, string fileName)
        {
            var md5 = MD5.Create();
            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            stream.Seek(0, SeekOrigin.Begin);
            var hash = Convert.ToBase64String(md5.ComputeHash(buffer));
            hash = hash.Replace('+', '-').Replace('/', '_').Remove(12);
            UploadResult result;
            result = CheckExists(hash);
            if (result.IsSuccess)
                return result;
            try
            {
                result = UploadData(stream, "https://mediacru.sh/api/upload/file", fileName, suppressWebExceptions: false);
            }
            catch (WebException e)
            {
                var response = e.Response as HttpWebResponse;
                if (response == null)
                    throw;
                if (response.StatusCode == HttpStatusCode.Conflict)
                    return HandleDuplicate(response);
                throw;
            }
            hash = JToken.Parse(result.Response)["hash"].Value<string>();
            while (true)
            {
                var request = (HttpWebRequest)WebRequest.Create("https://mediacru.sh/api/" + hash + "/status");
                var httpResponse = request.GetResponse();
                JToken response;
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                    response = JToken.Parse(streamReader.ReadToEnd());
                var status = response["status"].Value<string>();
                var done = !(status == "processing" || status == "pending");
                if (!done)
                {
                    Thread.Sleep(1000);
                    continue;
                }
                if (status == "done" || status == "ready")
                {
                    var blob = response[hash].ToObject<MediaCrushBlob>();
                    return new UploadResult
                    {
                        DeletionURL = "https://mediacru.sh/" + blob.Hash + "/delete",
                        IsSuccess = true,
                        ThumbnailURL = "https://mediacru.sh" + blob.Files[0].Path,
                        URL = blob.Url,
                        IsURLExpected = false
                    };
                }
                else
                {
                    switch (status)
                    {
                        case "unrecognized":
                            // Note: MediaCrush accepts just about _every_ kind of media file,
                            // so the file itself is probably corrupted or just not actually a media file
                            throw new Exception("This file is not an acceptable file type.");
                        case "timeout":
                            throw new Exception("This file took too long to process.");
                        default:
                            throw new Exception("This file failed to process.");
                    }
                }
            }
        }

        public UploadResult HandleDuplicate(HttpWebResponse httpResponse)
        {
            JToken response;
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                response = JToken.Parse(streamReader.ReadToEnd());
            var hash = response["hash"].Value<string>();
            var blob = response[hash].ToObject<MediaCrushBlob>();
            return new UploadResult
            {
                DeletionURL = "https://mediacru.sh/" + blob.Hash + "/delete",
                IsSuccess = true,
                ThumbnailURL = "https://mediacru.sh" + blob.Files[0].Path,
                URL = blob.Url,
                IsURLExpected = false
            };
        }

        private UploadResult CheckExists(string hash)
        {
            var request = (HttpWebRequest)WebRequest.Create("https://mediacru.sh/api/" + hash);
            try
            {
                var httpResponse = request.GetResponse();
                JToken response;
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                    response = JToken.Parse(streamReader.ReadToEnd());
                var blob = response.ToObject<MediaCrushBlob>();
                return new UploadResult
                {
                    DeletionURL = "https://mediacru.sh/" + blob.Hash + "/delete",
                    IsSuccess = true,
                    ThumbnailURL = "https://mediacru.sh" + blob.Files[0].Path,
                    URL = blob.Url,
                    IsURLExpected = false,
                    Response = blob.ToString()
                };
            }
            catch
            {
                return new UploadResult { IsSuccess = false };
            }
        }
    }

    internal class MediaCrushBlob
    {
        public class File
        {
            [JsonProperty("file")]
            public string Path { get; set; }
            [JsonProperty("type")]
            public string Mimetype { get; set; }
        }

        [JsonProperty("blob_type")]
        public string BlobType { get; set; }
        [JsonProperty("compression")]
        public double Compression { get; set; }
        [JsonProperty("files")]
        public File[] Files { get; set; }
        [JsonProperty("extras")]
        public File[] Extras { get; set; }
        [JsonProperty("original")]
        public string Original { get; set; }
        [JsonProperty("type")]
        public string UserMimetype { get; set; }
        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonIgnore]
        public string Url
        {
            get
            {
                return "https://mediacru.sh/" + Hash;
            }
        }
    }
}

