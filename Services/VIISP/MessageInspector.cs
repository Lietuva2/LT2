using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml.Xsl;
using Data.Infrastructure.Sessions;
using Framework.Hashing;
using Framework.Infrastructure;
using Framework.Infrastructure.Logging;
using Org.BouncyCastle.Security;
using Services.Enums;
using Services.Infrastructure;
using Services.ModelServices;

namespace Services.VIISP
{
    public class SimpleMessageInspector : IClientMessageInspector, IDispatchMessageInspector
    {
        public ReportingService ReportingService { get { return ServiceLocator.Resolve<ReportingService>(); } }

        #region IClientMessageInspector Members

        public void AfterReceiveReply(ref System.ServiceModel.Channels.Message reply, object correlationState)
        {
            MessageBuffer buffer = reply.CreateBufferedCopy(int.MaxValue);
            Message copy = buffer.CreateMessage();

            var logger = Log4NetLogger.Configure();
            var replyString = reply.ToString();
            logger.Information(replyString);
            ReportingService.LogUserActivity(replyString, LogTypes.BankLink);

            var doc = GetDocumentFromMessage(copy);
            if (!VerifySignature(doc))
            {
                throw new SignatureException("Signature is not valid");
            }

            reply = buffer.CreateMessage();
        }

        public object BeforeSendRequest(ref System.ServiceModel.Channels.Message request, System.ServiceModel.IClientChannel channel)
        {
            request = TransformMessage(request);

            MessageBuffer buffer = request.CreateBufferedCopy(int.MaxValue);
            Message copy = buffer.CreateMessage();

            var logger = Log4NetLogger.Configure();
            logger.Information(GetDocumentFromMessage(buffer.CreateMessage()).OuterXml);

            request = buffer.CreateMessage();

            return null;
        }

        private XmlDocument GetDocumentFromMessage(Message message)
        {
            MessageBuffer msgbuf = message.CreateBufferedCopy(int.MaxValue);

            Message tmpMessage = msgbuf.CreateMessage();
            XmlDictionaryReader xdr = tmpMessage.GetReaderAtBodyContents();

            XmlDocument xdoc = new XmlDocument();
            xdoc.Load(xdr);
            xdr.Close();

            return xdoc;
        }

        //only read and modify the Message Body part
        private Message TransformMessage(Message oldMessage)
        {
            Message newMessage = null;
            
            XmlDocument xdoc = GetDocumentFromMessage(oldMessage);

            var newDoc = TransformNamespaces(xdoc);
            var signature = SignDocument(newDoc, "#" + VIISPServiceOperations.GetNodeId());
            newDoc.DocumentElement.AppendChild(newDoc.ImportNode(signature, true));

            MemoryStream ms = new MemoryStream();
            XmlWriter xw = XmlWriter.Create(ms);
            newDoc.Save(xw);
            xw.Flush();
            xw.Close();

            ms.Position = 0;
            XmlReader xr = XmlReader.Create(ms);


            //create new message from modified XML document
            newMessage = Message.CreateMessage(oldMessage.Version, null, xr);
            newMessage.Headers.CopyHeadersFrom(oldMessage);
            newMessage.Properties.CopyProperties(oldMessage.Properties);

            return newMessage;
        }

        private static XmlDocument TransformNamespaces(XmlDocument xmlDoc)
        {
            XmlReader xsltReader = XmlReader.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream("Services.VIISP.NamespaceTransform.xslt"));
            XslCompiledTransform myXslTransform = new XslCompiledTransform();
            myXslTransform.Load(xsltReader);

            var nav = xmlDoc.CreateNavigator();

            StringWriter sw = new StringWriter();
            myXslTransform.Transform(nav, null, sw);

            var newXml = new XmlDocument();
            newXml.LoadXml(sw.ToString());
            return newXml;
        }

        private static XmlElement SignDocument(XmlDocument xmlDoc, string uri)
        {
            if (xmlDoc == null)
                throw new ArgumentException("xmlDoc");

            RSACryptoServiceProvider Key = null;
            System.Security.Cryptography.X509Certificates.X509Certificate2 certificate;
            try
            {
                if (HttpContext.Current.IsDebuggingEnabled)
                {
                    certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                        HttpContext.Current.Server.MapPath("~/Certificates/testcert.pfx"), "testtest",
                        X509KeyStorageFlags.MachineKeySet);
                }
                else
                {
                    certificate = DigitalSignature.GetStoreCertificate();
                }

                Key = (RSACryptoServiceProvider)certificate.PrivateKey;
            }
            catch (CryptographicException ex)
            {
                Trace.TraceInformation(String.Format("CryptoException: {0}, Stack Trace: {1}, Source: {2}", ex.Message, ex.StackTrace, ex.Source));
                if (ex.InnerException != null)
                    Trace.TraceInformation(ex.InnerException.Message);
                throw ex;
            }

            if (Key == null)
                throw new ArgumentException("Key");



            SignedXml signedXml = new SignedXml(xmlDoc);
            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            signedXml.SigningKey = Key;

            var prefix = xmlDoc.DocumentElement.Prefix;
            XmlDsigExcC14NTransform canMethod = (XmlDsigExcC14NTransform)signedXml.SignedInfo.CanonicalizationMethodObject;
            if (xmlDoc.DocumentElement != null && !String.IsNullOrEmpty(prefix))
                canMethod.InclusiveNamespacesPrefixList = prefix;

            Reference reference = new Reference();
            reference.Uri = uri;

            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            XmlDsigExcC14NTransform trans2 = new XmlDsigExcC14NTransform();
            if (xmlDoc.DocumentElement != null && !String.IsNullOrEmpty(prefix))
                trans2.InclusiveNamespacesPrefixList = prefix;
            reference.AddTransform(trans2);

            signedXml.AddReference(reference);

            KeyInfo info = new KeyInfo();
            info.AddClause(new RSAKeyValue((RSA)Key));
            signedXml.KeyInfo = info;

            signedXml.ComputeSignature();

            return signedXml.GetXml();
        }

        public static bool VerifySignature(XmlDocument doc)
        {
            SignedXml xml = new SignedXml(doc);
            XmlNodeList elementsByTagName = doc.GetElementsByTagName("Signature");
            if (elementsByTagName.Count == 0)
            {
                return true;
            }

            xml.LoadXml((XmlElement) elementsByTagName[0]);
            var key = new X509Certificate2(HttpContext.Current.Server.MapPath("~/Certificates/epaslaugos_ident.crt"));
            return xml.CheckSignature(key, true);
        }

        #endregion



        #region IDispatchMessageInspector Members

        public object AfterReceiveRequest(ref System.ServiceModel.Channels.Message request, System.ServiceModel.IClientChannel channel, System.ServiceModel.InstanceContext instanceContext)
        {
            return null;
        }

        public void BeforeSendReply(ref System.ServiceModel.Channels.Message reply, object correlationState)
        { }

        #endregion
    }
}
