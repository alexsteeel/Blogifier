﻿using Core.Data;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace Core.Services
{
    public interface IStorageService
    {
        string Location { get; }
        
        void CreateFolder(string path);
        void DeleteFolder(string path);
        
        Task<AssetItem> UploadFormFile(IFormFile file, string root, string path = "");
        Task<AssetItem> UploadBase64Image(string baseImg, string root, string path = "");
        Task<AssetItem> UploadFromWeb(Uri requestUri, string root, string path = "");
        void DeleteFile(string path);

        IList<string> GetAssets(string path);
        IList<string> GetThemes();

        Task<IEnumerable<AssetItem>> Find(Func<AssetItem, bool> predicate, Pager pager, string path = "");
    }

    public class StorageService : IStorageService
    {
        string _blogSlug;
        string _separator = Path.DirectorySeparatorChar.ToString();
        string _uploadFolder = "data";
        IHttpContextAccessor _httpContext;

        public StorageService(IHttpContextAccessor httpContext)
        {
            if(httpContext == null || httpContext.HttpContext == null)
            {
                _blogSlug = "";
            }
            else
            {
                _blogSlug = httpContext.HttpContext.User.Identity.Name;
            }
            
            _httpContext = httpContext;

            if (!Directory.Exists(Location))
                CreateFolder("");
        }

        public string Location
        {
            get
            {
                var path = AppSettings.WebRootPath == null ?
                    Path.Combine(GetAppRoot(), "wwwroot") : AppSettings.WebRootPath;

                path = Path.Combine(path, _uploadFolder.Replace("/", Path.DirectorySeparatorChar.ToString()));

                if (!string.IsNullOrEmpty(_blogSlug))
                {
                    path = Path.Combine(path, _blogSlug);
                }
                return path;
            }
        }

        public IList<string> GetAssets(string path)
        {
            path = path.Replace("/", _separator);
            try
            {
                var dir = string.IsNullOrEmpty(path) ? Location : Path.Combine(Location, path);
                var items = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                return new List<string>(items);
            }
            catch { }
            return null;
        }

        public IList<string> GetThemes()
        {
            var items = new List<string>();
            var dir = Path.Combine(GetAppRoot(), $"Views{_separator}Themes");
            try
            {
                foreach (string d in Directory.GetDirectories(dir))
                    items.Add(Path.GetFileName(d));
            }
            catch { }
            return items;
        }

        public async Task<AssetItem> UploadFormFile(IFormFile file, string root, string path = "")
        {
            path = path.Replace("/", _separator);

            VerifyPath(path);

            var fileName = GetFileName(file.FileName);
            var filePath = string.IsNullOrEmpty(path) ?
                Path.Combine(Location, fileName) :
                Path.Combine(Location, path + _separator + fileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
                return new AssetItem
                {
                    Title = fileName,
                    Path = TrimFilePath(filePath),
                    Url = GetUrl(filePath, root)
                };
            }
        }

        public async Task<AssetItem> UploadBase64Image(string baseImg, string root, string path = "")
        {
            path = path.Replace("/", _separator);
            var fileName = "";

            VerifyPath(path);

            Random rnd = new Random();

            if (baseImg.StartsWith("data:image/png;base64,"))
            {
                fileName = string.Format("{0}.png", rnd.Next(1000, 9999));
                baseImg = baseImg.Replace("data:image/png;base64,", "");
            }
            if (baseImg.StartsWith("data:image/jpeg;base64,"))
            {
                fileName = string.Format("{0}.jpeg", rnd.Next(1000, 9999));
                baseImg = baseImg.Replace("data:image/jpeg;base64,", "");
            }
            if (baseImg.StartsWith("data:image/gif;base64,"))
            {
                fileName = string.Format("{0}.gif", rnd.Next(1000, 9999));
                baseImg = baseImg.Replace("data:image/gif;base64,", "");
            }

            var filePath = string.IsNullOrEmpty(path) ?
                Path.Combine(Location, fileName) :
                Path.Combine(Location, path + _separator + fileName);

            byte[] bytes = Convert.FromBase64String(baseImg);

            await File.WriteAllBytesAsync(filePath, Convert.FromBase64String(baseImg));

            return new AssetItem
            {
                Title = fileName,
                Path = filePath,
                Url = GetUrl(filePath, root)
            };
        }

        public async Task<AssetItem> UploadFromWeb(Uri requestUri, string root, string path = "")
        {
            path = path.Replace("/", _separator);

            VerifyPath(path);

            var fileName = TitleFromUri(requestUri);
            var filePath = string.IsNullOrEmpty(path) ?
                Path.Combine(Location, fileName) :
                Path.Combine(Location, path + _separator + fileName);

            using (var client = new HttpClient())
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    using (
                        Stream contentStream = await (await client.SendAsync(request)).Content.ReadAsStreamAsync(),
                        stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 3145728, true))
                    {
                        await contentStream.CopyToAsync(stream);
                        return new AssetItem
                        {
                            Title = fileName,
                            Path = filePath,
                            Url = GetUrl(filePath, root)
                        };
                    }
                }
            }
        }

        public async Task<IEnumerable<AssetItem>> Find(Func<AssetItem, bool> predicate, Pager pager, string path = "")
        {
            var skip = pager.CurrentPage * pager.ItemsPerPage - pager.ItemsPerPage;
            var files = GetAssets(path);
            var items = MapFilesToAssets(files);

            if (predicate != null)
                items = items.Where(predicate).ToList();

            pager.Configure(items.Count);

            var page = items.Skip(skip).Take(pager.ItemsPerPage).ToList();

            return await Task.FromResult(page);
        }

        public void CreateFolder(string path)
        {
            var dir = GetFullPath(path);

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public void DeleteFolder(string path)
        {
            var dir = GetFullPath(path);

            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        public void DeleteFile(string path)
        {
            path = path.Replace($"{_uploadFolder}{_separator}{_blogSlug}{_separator}", "");
            File.Delete(GetFullPath(path));
        }

        void VerifyPath(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var dir = Path.Combine(Location, path);

                if (!Directory.Exists(dir))
                {
                    CreateFolder(dir);
                }
            }
        }

        string TrimFilePath(string path)
        {
            var p = path.Replace(AppSettings.WebRootPath, "");
            if (p.StartsWith("\\")) p = p.Substring(1);
            return p;
        }

        string GetFullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Location;
            else
                return Path.Combine(Location, path.Replace("/", _separator));
        }

        string GetFileName(string fileName)
        {
            // some browsers pass uploaded file name as short file name 
            // and others include the path; remove path part if needed
            if (fileName.Contains(_separator))
            {
                fileName = fileName.Substring(fileName.LastIndexOf(_separator));
                fileName = fileName.Replace(_separator, "");
            }
            // when drag-and-drop or copy image to TinyMce editor
            // it uses "mceclip0" as file name; randomize it for multiple uploads
            if (fileName.StartsWith("mceclip0"))
            {
                Random rnd = new Random();
                fileName = fileName.Replace("mceclip0", rnd.Next(100000, 999999).ToString());
            }
            return fileName;
        }

        string GetUrl(string path, string root)
        {
            var url = path.ReplaceIgnoreCase(Location, "").Replace(_separator, "/");
            return string.Concat(_uploadFolder, "/", _blogSlug, url);
        }

        string GetAppRoot()
        {
            // normal application run
            if(!string.IsNullOrEmpty(AppSettings.ContentRootPath))
                return AppSettings.ContentRootPath;

            // unit tests run
            var assembly = Assembly.Load(new AssemblyName("Tests"));
            var uri = new UriBuilder(assembly.CodeBase);
            var path = Uri.UnescapeDataString(uri.Path);
            var root = Path.GetDirectoryName(path);
            root = root.Substring(0, root.IndexOf("Tests")); //.Replace("tests\\", "");

            return Path.Combine(root, "App");
        }

        string TitleFromUri(Uri uri)
        {
            var title = uri.ToString().ToLower();
            title = title.Replace("%2f", "/");

            if (title.EndsWith(".axdx"))
            {
                title = title.Replace(".axdx", "");
            }
            if (title.Contains("image.axd?picture="))
            {
                title = title.Substring(title.IndexOf("image.axd?picture=") + 18);
            }
            if (title.Contains("file.axd?file="))
            {
                title = title.Substring(title.IndexOf("file.axd?file=") + 14);
            }
            if (title.Contains("encrypted-tbn") || title.Contains("base64,"))
            {
                Random rnd = new Random();
                title = string.Format("{0}.png", rnd.Next(1000, 9999));
            }

            if (title.Contains("/"))
            {
                title = title.Substring(title.LastIndexOf("/"));
            }

            return title.Replace("/", "");
        }

        List<AssetItem> MapFilesToAssets(IList<string> assets)
        {
            var items = new List<AssetItem>();

            foreach (var asset in assets)
            {
                items.Add(new AssetItem {
                    Path = asset,
                    Url = pathToUrl(asset),
                    Title = pathToTitle(asset),
                    Image = pathToImage(asset)
                });
            }

            return items;
        }

        string pathToUrl(string path)
        {
            return path.Substring(path.IndexOf("wwwroot") + 8)
                .Replace(_separator, "/");
        }

        string pathToTitle(string path)
        {
            var title = path;

            if(title.LastIndexOf(_separator) > 0)
                title = title.Substring(title.LastIndexOf(_separator));       

            if(title.IndexOf('.') > 0)
                title = title.Substring(1, title.IndexOf('.'));

            return title;
        }

        string pathToImage(string path)
        {
            if(path.IsImagePath())
                return pathToUrl(path);

            var ext = "blank.png";

            if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                ext = "xml.png";

            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ext = "zip.png";

            if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                ext = "txt.png";

            if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                ext = "pdf.png";

            if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                ext = "mp3.png";

            if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                ext = "mp4.png";

            if (path.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                ext = "doc.png";

            if (path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                ext = "xls.png";

            return $"lib/img/doctypes/{ext}";
        }
    }
}