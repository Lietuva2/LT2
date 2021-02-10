/*
 * Copyright (c) 2012 Google Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except
 * in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Mvc;
using Google.Apis.Drive.v2;
using Google.Apis.Util.Store;

using Services.Infrastructure;
using Services.Session;

namespace Web.Infrastructure.Google
{
    public class SessionDataStore : IDataStore
    {
        private Dictionary<string, object> dictionary
        {
            get
            {
                var dict = controller.Session["GoogleDataStore"] as Dictionary<string, object>;
                if (dict == null)
                {
                    controller.Session["GoogleDataStore"] = new Dictionary<string, object>();
                }

                return controller.Session["GoogleDataStore"] as Dictionary<string, object>;
            }
            set
            {
                controller.Session["GoogleDataStore"] = value;
            }
        }

        private readonly Controller controller;

        public SessionDataStore(Controller controller)
        {
            this.controller = controller;
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            dictionary[key] = value;
        }

        public async Task DeleteAsync<T>(string key)
        {
            dictionary.Remove(key);
        }

        public async Task<T> GetAsync<T>(string key)
        {
            if (!dictionary.ContainsKey(key))
            {
                return default(T);
            }

            return (T)dictionary[key];
        }

        public async Task ClearAsync()
        {
            dictionary = null;
        }
    }

    public class AppFlowMetadata : FlowMetadata
    {
        private IAuthorizationCodeFlow flow;

        private string callbackUrl;

        private Controller controller;

        public AppFlowMetadata(Controller controller)
        {
            this.flow =
                new GoogleAuthorizationCodeFlow(
                    new GoogleAuthorizationCodeFlow.Initializer
                        {
                            ClientSecrets =
                                new ClientSecrets
                                    {
                                        ClientId =
                                            CustomAppSettings
                                            .GoogleAppId,
                                        ClientSecret =
                                            CustomAppSettings
                                            .GoogleAppSecret
                                    },
                            Scopes =
                                new[]
                                    {
                                        DriveService.Scope.Drive,
                                        DriveService.Scope.DriveFile
                                    },
                            DataStore = new SessionDataStore(controller)
                        });
            callbackUrl = controller.Url.Action(MVC.AuthCallback.IndexAsync());
            this.controller = controller;
        }

        public override string AuthCallback
        {
            get
            {
                return callbackUrl;
            }
        }

        public override string GetUserId(Controller controller)
        {
            // In this sample we use the session to store the user identifiers.
            // That's not the best practice, because you should have a logic to identify
            // a user. You might want to use "OpenID Connect".
            // You can read more about the protocol in the following link:
            // https://developers.google.com/accounts/docs/OAuth2Login.
            //var user = controller.Session["google.user"];
            //if (user == null)
            //{
            //    user = Guid.NewGuid();
            //    controller.Session["google.user"] = user;
            //}
            //return user.ToString();
            return MembershipSession.GetUser(controller.Session, controller.User.Identity).Id;
        }

        public override IAuthorizationCodeFlow Flow
        {
            get { return flow; }
        }
    }
}
