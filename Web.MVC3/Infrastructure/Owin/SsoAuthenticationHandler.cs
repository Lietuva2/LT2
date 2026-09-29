// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Framework.Infrastructure.Logging;
using Framework.Mvc.Security;
using Microsoft.Owin;
using Microsoft.Owin.Infrastructure;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;
using Services.Infrastructure;

namespace Web.Infrastructure.Owin
{
    public class SsoAuthenticationHandler : AuthenticationHandler<SsoAuthenticationOptions>
    {
        private const string XmlSchemaString = "http://www.w3.org/2001/XMLSchema#string";
        private readonly ILogger _logger = MvcApplication.Logger;

        public SsoAuthenticationHandler()
        {
        }

        protected override async Task<AuthenticationTicket> AuthenticateCoreAsync()
        {
            AuthenticationProperties properties = null;

            try
            {
                string encryptedUser = null;
                string redirectTo = null;

                IReadableStringCollection query = Request.Query;

                IList<string> values = query.GetValues("error");
                if (values != null && values.Count >= 1)
                {
                    _logger.Error("Remote server returned an error: " + Request.QueryString);
                }

                values = query.GetValues("user");
                if (values != null && values.Count == 1)
                {
                    encryptedUser = values[0];
                }

                values = query.GetValues("redirectTo");
                if (values != null && values.Count == 1 && IsSameSiteUrl(values[0]))
                {
                    redirectTo = values[0];
                }

                if (encryptedUser == null)
                {
                    _logger.Error("user info was not provided");
                    return null;
                }

                // token: username;UTC timestamp (round-trip format);random nonce – see AccountController.GetSsoUserInfo
                var user = SecurityUtil.Decrypt(encryptedUser, CustomAppSettings.SsoSharedSecret).Split(';');
                if (user.Length != 3)
                {
                    _logger.Error("invalid SSO token");
                    return null;
                }

                var username = user[0];
                DateTime timeStamp;
                if (!DateTime.TryParseExact(user[1], "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timeStamp))
                {
                    _logger.Error("invalid SSO token timestamp");
                    return null;
                }

                var age = DateTime.UtcNow - timeStamp.ToUniversalTime();
                if (age > TimeSpan.FromSeconds(30) || age < TimeSpan.FromSeconds(-30))
                {
                    _logger.Warning("Timestamp has expired");
                    return null;
                }

                // each token can be used once
                var replayKey = "sso-token:" + user[2];
                if (HttpRuntime.Cache.Add(replayKey, true, null, DateTime.UtcNow.AddMinutes(2),
                        System.Web.Caching.Cache.NoSlidingExpiration, System.Web.Caching.CacheItemPriority.High, null) != null)
                {
                    _logger.Warning("SSO token was already used");
                    return null;
                }

                var identity = new ClaimsIdentity(
                    Options.AuthenticationType,
                    ClaimsIdentity.DefaultNameClaimType,
                    ClaimsIdentity.DefaultRoleClaimType);

                if (!string.IsNullOrEmpty(username))
                {
                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, username, XmlSchemaString, Options.AuthenticationType));
                }
                
                return new AuthenticationTicket(identity, new AuthenticationProperties()
                {
                    RedirectUri = redirectTo
                });
            }
            catch (Exception ex)
            {
                _logger.Error("Authentication failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Only redirect back to this site after an SSO login (relative path or an absolute URL on this host).
        /// </summary>
        private bool IsSameSiteUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            if (url.StartsWith("/"))
            {
                return !url.StartsWith("//") && !url.StartsWith("/\\");
            }

            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                   string.Equals(uri.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase);
        }

        protected override async Task ApplyResponseChallengeAsync()
        {
            if (Response.StatusCode != 401)
            {
                return ;
            }

            AuthenticationResponseChallenge challenge = Helper.LookupChallenge(Options.AuthenticationType, Options.AuthenticationMode);

            if (challenge != null)
            {


                string baseUri =
                    Request.Scheme +
                    Uri.SchemeDelimiter +
                    Request.Host +
                    Request.PathBase;

                string currentUri =
                    baseUri +
                    Request.Path +
                    Request.QueryString;

                string redirectUri =
                    baseUri +
                    Options.CallbackPath;

                AuthenticationProperties properties = challenge.Properties;
                if (string.IsNullOrEmpty(properties.RedirectUri))
                {
                    properties.RedirectUri = currentUri;
                }

                redirectUri += "?redirectTo=" + properties.RedirectUri;


                string authorizationEndpoint =
                    CustomAppSettings.SsoBaseUrl + "/Account/SsoLogin" +
                    "?returnUrl=" + Uri.EscapeDataString(redirectUri);

                Context.Response.Redirect(authorizationEndpoint);
            }
        }

        public override async Task<bool> InvokeAsync()
        {
            return await InvokeReplyPathAsync();
        }

        private async Task<bool> InvokeReplyPathAsync()
        {
            if (Options.CallbackPath.HasValue && Options.CallbackPath == Request.Path)
            {
                // TODO: error responses

                AuthenticationTicket ticket = await AuthenticateAsync();
                if (ticket == null)
                {
                    _logger.Warning("Invalid return state, unable to redirect.");
                    Response.StatusCode = 500;
                    return true;
                }

                if (Options.SignInAsAuthenticationType != null &&
                    ticket.Identity != null)
                {
                    ClaimsIdentity grantIdentity = ticket.Identity;
                    if (!string.Equals(grantIdentity.AuthenticationType, Options.SignInAsAuthenticationType, StringComparison.Ordinal))
                    {
                        grantIdentity = new ClaimsIdentity(grantIdentity.Claims, Options.SignInAsAuthenticationType, grantIdentity.NameClaimType, grantIdentity.RoleClaimType);
                    }

                    Context.Authentication.SignIn(ticket.Properties, grantIdentity);
                }

                if (ticket.Properties.RedirectUri != null)
                {
                    string redirectUri = ticket.Properties.RedirectUri;
                    if (ticket.Identity == null)
                    {
                        // add a redirect hint that sign-in failed in some way
                        redirectUri = WebUtilities.AddQueryString(redirectUri, "error", "access_denied");
                    }

                    Response.Redirect(redirectUri);
                    return true;
                }
            }

            return false;
        }
    }
}
