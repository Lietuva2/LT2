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
using System.Web;
using DotNetOpenAuth.OAuth2;
using Google.Apis.Drive.v2;
using Google.Apis.Drive.v2.Data;
using Google;
using System.Collections.Specialized;
using System.Net;
using System.IO;
using System.Text;
using System.Threading;

using DotNetOpenAuth.Messaging;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;

using File = Google.Apis.Drive.v2.Data.File;

namespace Web.Infrastructure.Google
{
    public class GoogleAuthUtils
    {
        /// <summary>
        /// Update both metadata and content of a file and return the updated file.
        /// </summary>
        public static bool UpdatePermissions(DriveService service, string fileId)
        {
            // First retrieve the file from the API.
            File file = service.Files.Get(fileId).Execute();


            var p = new Permission()
            {
                Role = "reader",
                Type = "anyone",
                WithLink = true
            };

            var perm = service.Permissions.Insert(p, file.Id).Execute();

            perm.AdditionalRoles = new List<string>() { "commenter" };

            service.Permissions.Patch(perm, file.Id, perm.Id).Execute();

            return true;
        }

        /// <summary>
        /// Create a new file and return it.
        /// </summary>
        public static File Create(DriveService service, String title)
        {
            // File's metadata.
            File body = new File();
            body.Title = title;
            body.MimeType = "application/vnd.google-apps.document";
            var request = service.Files.Insert(body);

            request.Convert = true;
            var file = request.Execute();

            var p = new Permission()
            {
                Role = "reader",
                Type = "anyone",
                WithLink = true
            };

            var perm = service.Permissions.Insert(p, file.Id).Execute();

            perm.AdditionalRoles = new List<string>() { "commenter" };

            service.Permissions.Patch(perm, file.Id, perm.Id).Execute();

            return file;
        }
    }
}
