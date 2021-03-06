﻿using KissLog.Internal;
using KissLog.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace KissLog.AspNet.Web
{
    internal static class WebRequestPropertiesFactory
    {
        private static readonly string[] ServerVariablesKeysToIgnore = {"all_http", "all_raw"};

        public static WebRequestProperties Create(HttpRequest request)
        {
            WebRequestProperties result = new WebRequestProperties();

            if (request == null)
                return result;

            result.StartDateTime = DateTime.UtcNow;
            result.UserAgent = request.UserAgent;
            result.Url = request.Url;
            result.HttpMethod = request.HttpMethod;
            result.HttpReferer = request.UrlReferrer?.AbsolutePath;
            result.RemoteAddress = request.UserHostAddress;
            result.MachineName = GetMachineName(request);

            RequestProperties requestProperties = new RequestProperties();
            result.Request = requestProperties;

            var headers = DataParser.ToDictionary(request.Unvalidated.Headers);
            headers = FilterHeaders(headers);

            var queryString = DataParser.ToDictionary(request.Unvalidated.QueryString);
            var formData = DataParser.ToDictionary(request.Unvalidated.Form);
            var serverVariables = DataParser.ToDictionary(request.ServerVariables);
            serverVariables = FilterServerVariables(serverVariables);

            var cookies = DataParser.ToDictionary(request.Unvalidated.Cookies);

            requestProperties.Headers = headers;
            requestProperties.QueryString = queryString;
            requestProperties.FormData = formData;
            requestProperties.ServerVariables = serverVariables;
            requestProperties.Cookies = cookies;
            string inputStream = null;

            if (InternalHelpers.ShouldLogInputStream(headers))
            {
                inputStream = ReadInputStream(request);
            }

            requestProperties.InputStream = inputStream;

            return result;
        }

        private static string GetMachineName(HttpRequest request)
        {
            string machineName = string.Empty;

            try
            {
                machineName = Environment.MachineName;
            }
            catch
            {
                // ignored
            }

            if (string.IsNullOrEmpty(machineName))
            {
                if (!string.IsNullOrEmpty(request.ServerVariables["SERVER_NAME"]))
                {
                    machineName = request.ServerVariables["SERVER_NAME"];
                }
            }

            return machineName;
        }

        private static string ReadInputStream(HttpRequest request)
        {
            string content = string.Empty;
            if (request.InputStream.CanRead == false)
                return content;

            using (MemoryStream ms = new MemoryStream())
            {
                request.InputStream.CopyTo(ms);

                request.InputStream.Position = 0;
                ms.Position = 0;

                using (StreamReader readStream = new StreamReader(ms, request.ContentEncoding))
                {
                    content = readStream.ReadToEndAsync().Result;
                }
            }

            return content;
        }

        private static List<KeyValuePair<string, string>> FilterHeaders(List<KeyValuePair<string, string>> values)
        {
            if (values == null || !values.Any())
                return values;

            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();

            foreach (var item in values)
            {
                if (string.IsNullOrEmpty(item.Key))
                    continue;

                if (string.Compare(item.Key, "Cookie", StringComparison.OrdinalIgnoreCase) == 0)
                    continue;

                result.Add(new KeyValuePair<string, string>(item.Key, item.Value));
            }

            return result;
        }

        private static List<KeyValuePair<string, string>> FilterServerVariables(List<KeyValuePair<string, string>> values)
        {
            if (values == null || !values.Any())
                return values;

            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();

            foreach (var item in values)
            {
                if (string.IsNullOrEmpty(item.Key))
                    continue;

                string key = item.Key.ToLower();

                if (ServerVariablesKeysToIgnore.Contains(key))
                    continue;

                if (key.StartsWith("http_"))
                    continue;

                result.Add(new KeyValuePair<string, string>(item.Key, item.Value));
            }

            return result;
        }
    }
}
