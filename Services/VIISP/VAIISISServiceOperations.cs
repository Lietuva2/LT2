using System;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Xml;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Security.Cryptography;
using System.IO;
using System.Diagnostics;
using System.Xml.Serialization;
using System.Configuration;
using System.Xml.Xsl;
using Framework.Infrastructure.Logging;
using Services.Infrastructure;

namespace Services.VIISP
{
    public class VIISPServiceOperations : IDisposable
    {
        private AuthenticationService proxy;
        private static string uniqueNodeId = "uniqueNodeId";

        #region private methods

        public VIISPServiceOperations()
        {
            AuthenticationServiceClient client = new AuthenticationServiceClient("ExternalAuthenticationServiceImplPort");
            client.Endpoint.Behaviors.Add(
                new SimpleEndpointBehavior()
                );
            client.Open();
            proxy = client;
        }

        public void Dispose()
        {
            if ((proxy as AuthenticationServiceClient) != null)
            {
                (proxy as AuthenticationServiceClient).Close();
                proxy = null;
            }
        }

        private initAuthentication CreateInitRequest(string customData = null)
        {
            authenticationRequest request = new authenticationRequest();
            request.id = uniqueNodeId;
            initAuthentication itm = new initAuthentication(request);
            itm.authenticationRequest.pid = CustomAppSettings.ViispPid;
            itm.authenticationRequest.customData = customData;
            itm.authenticationRequest.postbackUrl = CustomAppSettings.ViispPostBack;
            itm.authenticationRequest.userInformation = new userInformation[]
            {
                userInformation.firstName,
                userInformation.lastName,
                userInformation.companyName
            };
            if (HttpContext.Current.IsDebuggingEnabled)
            {
                itm.authenticationRequest.authenticationProvider = new authenticationProvider[]
                {
                    authenticationProvider.authltidentitycard,
                    authenticationProvider.authltbank
                };
            }
            else
            {
                itm.authenticationRequest.authenticationProvider = new authenticationProvider[]
                {
                    authenticationProvider.authltidentitycard,
                    authenticationProvider.authltbank,
                    authenticationProvider.authsignatureProvider,
                    authenticationProvider.authltgovernmentemployeecard
                };
            };

            itm.authenticationRequest.authenticationAttribute = new authenticationAttribute[]
            {
                authenticationAttribute.ltpersonalcode,
                authenticationAttribute.ltcompanycode,
                authenticationAttribute.ltgovernmentemployeecode
            };

            return itm;
        }

        private getAuthenticationData CreateAuthDataRequest(string ticketId)
        {
            authenticationDataRequest request = new authenticationDataRequest();
            request.ticket = ticketId;
            request.pid = CustomAppSettings.ViispPid;
            request.id = uniqueNodeId;

            return new getAuthenticationData(request);
        }
        #endregion

        #region public methods

        public static string GetNodeId()
        {
            return uniqueNodeId;
        }

        public string GetAuthTicket(string customData = null)
        {
            initAuthentication itm = CreateInitRequest(customData);
            initAuthenticationResponse response = proxy.initAuthentication(itm);

            if (response == null || response.authenticationResponse == null) return String.Empty;
            return response.authenticationResponse.ticket;
        }

        public getAuthenticationDataResponse GetAuthData(string ticket)
        {
            getAuthenticationData data = CreateAuthDataRequest(ticket);
            getAuthenticationDataResponse response = proxy.getAuthenticationData(data);

            return response;
        }
        #endregion
     }
}
