﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Security.Claims;
using System.Text;
using System.Web;
using KissLog.Web;
using KissLog;

namespace KissLog.AspNet.Web
{
    public class KissLogHttpModule : IHttpModule
    {
        public void Init(HttpApplication context)
        {
            context.BeginRequest += BeginRequest;
            context.EndRequest += EndRequest;
            context.Error += OnError;
            context.PreRequestHandlerExecute += Context_PreRequestHandlerExecute;
            context.PostAuthenticateRequest += PostAuthenticateRquest;
            context.PostAcquireRequestState += Context_PostAcquireRequestState;
        }

        private void Context_PreRequestHandlerExecute(object sender, EventArgs e)
        {
            var application = sender as HttpApplication;
            if(application == null)
                return;

            var context = application.Context;
            var response = context.Response;

            // Add a filter to capture response stream
            response.Filter = new ResponseSniffer(response.Filter, response.ContentEncoding);
        }

        private void Context_PostAcquireRequestState(object sender, EventArgs e)
        {
            HttpContext ctx = HttpContext.Current;

            if (ctx.Session == null)
                return;

            // We need to add a dummy session value, otherwise ctx.Session.IsNewSession will always be true
            ctx.Session.Add("X-KissLogSession", true);

            WebRequestProperties requestProperties = (WebRequestProperties)ctx.Items[Constants.HttpRequestPropertiesKey];
            if (requestProperties == null)
                return;

            requestProperties.IsNewSession = ctx.Session.IsNewSession;
            requestProperties.SessionId = ctx.Session.SessionID;
        }

        private void PostAuthenticateRquest(object sender, EventArgs e)
        {
            HttpContext ctx = HttpContext.Current;

            WebRequestProperties requestProperties = (WebRequestProperties)ctx.Items[Constants.HttpRequestPropertiesKey];
            if(requestProperties == null)
                return;

            ClaimsPrincipal claimsPrincipal = (ClaimsPrincipal)ctx.User;
            ClaimsIdentity identity = (ClaimsIdentity) claimsPrincipal?.Identity;

            if (identity != null && identity.IsAuthenticated == true)
            {
                List<KeyValuePair<string, string>> claims = DataParser.ToDictionary(identity);

                string userName = claims.FirstOrDefault(p => KissLogConfiguration.UserNameClaims.Contains(p.Key.ToLower())).Value;
                string emailAddress = claims.FirstOrDefault(p => KissLogConfiguration.EmailAddressClaims.Contains(p.Key.ToLower())).Value;
                string avatar = claims.FirstOrDefault(p => KissLogConfiguration.UserAvatarClaims.Contains(p.Key.ToLower())).Value;

                requestProperties.Request.Claims = claims;

                requestProperties.IsAuthenticated = true;
                requestProperties.User = new UserDetails
                {
                    Name = userName,
                    EmailAddress = emailAddress,
                    Avatar = avatar
                };
            }
        }

        private void BeginRequest(object sender, EventArgs e)
        {
            HttpContext ctx = HttpContext.Current;
            var request = ctx.Request;

            ILogger logger = LoggerFactory.GetInstance(ctx);

            WebRequestProperties requestProperties = WebRequestPropertiesFactory.Create(request);
            ctx.Items[Constants.HttpRequestPropertiesKey] = requestProperties;
        }

        private void OnError(object sender, EventArgs eventArgs)
        {
            HttpContext ctx = HttpContext.Current;

            ILogger logger = LoggerFactory.GetInstance(ctx);

            if (logger == null)
                return;

            Exception ex = ctx.Server.GetLastError();
            if (ex != null)
            {
                logger.Log(LogLevel.Error, ex);
            }
        }

        private void EndRequest(object sender, EventArgs e)
        {
            HttpContext ctx = HttpContext.Current;

            ILogger logger = LoggerFactory.GetInstance(ctx);
            if (logger == null)
                return;

            Exception ex = ctx.Server.GetLastError();
            if (ex != null)
            {
                logger.Log(LogLevel.Error, ex);
            }

            if (ctx.Response.StatusCode >= 400 && ex == null)
            {
                var filter = ctx.Response.Filter as ResponseSniffer;
                if (filter != null)
                {
                    string response = filter.GetContent();
                    if (string.IsNullOrEmpty(response) == false)
                    {
                        logger.Log(LogLevel.Error, response);
                    }
                }
            }

            WebRequestProperties webRequestProperties = (WebRequestProperties)HttpContext.Current.Items[Constants.HttpRequestPropertiesKey];
            webRequestProperties.EndDateTime = DateTime.UtcNow;

            ResponseProperties responseProperties = new ResponseProperties();
            responseProperties.HttpStatusCode = (HttpStatusCode)ctx.Response.StatusCode;
            responseProperties.Headers = DataParser.ToDictionary(ctx.Response.Headers);

            webRequestProperties.Response = responseProperties;

            ((Logger) logger).WebRequestProperties = webRequestProperties;

            IEnumerable<ILogger> loggers = LoggerFactory.GetAll(ctx);

            Logger.NotifyListeners(loggers.ToArray());
        }

        public void Dispose()
        {

        }
    }
}
