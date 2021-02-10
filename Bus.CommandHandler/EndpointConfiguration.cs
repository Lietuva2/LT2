//using System;
//using System.Configuration;
//using Bus.CompletedCommandHandler;
//using Framework.Infrastructure;
//using Framework.Infrastructure.Logging;
//using Framework.Infrastructure.Storage;
//using MongoDB.Bson.Serialization;
//using MongoDB.Bson.Serialization.Conventions;
//using MongoDB.Bson.Serialization.Options;

//using Services.Infrastructure;
//using NServiceBus.Unicast.Transport.Transactional.Config;
//using Bootstrapper = Bus.CompletedCommandHandler.Bootstrapper;

//namespace Bus.CommandHandler
//{
//    /// <summary>
//    /// Service bus endpoint configuration.
//    /// </summary>
//    public class EndpointConfiguration : IConfigureThisEndpoint, AsA_Server, IWantCustomInitialization
//    {
//        /// <summary>
//        /// Gets or sets the service bus.
//        /// </summary>
//        /// <value>The service bus.</value>
//        public IBus Bus { get; set; }

//        /// <summary>
//        /// Inits this instance.
//        /// </summary>
//        public void Init()
//        {
//            SetLoggingLibrary.Log4Net(() => Log4NetLogger.Configure());

//            var kernel = Bootstrapper.RegisterServices();
//            ServiceLocator.Resolver = kernel.GetService;
//            var config = Configure.With().NinjectBuilder(kernel);

//            if (ConfigurationManager.AppSettings["EndpointName"] != null)
//            {
//                config = config.DefineEndpointName(ConfigurationManager.AppSettings["EndpointName"]);
//            }

//            MongoObjectIdGenerator.Register();
//            MongoObjectIdSerializer.Register();

//            BsonClassMap.RegisterConventions(new ConventionProfile().SetSerializationOptionsConvention(new LocalDateTimeSerializationConvention()), c => true);

//            config
//                .DisableSecondLevelRetries()
//                .XmlSerializer()
//                 .MsmqTransport()
//                    .IsTransactional(true)
//                    .SupressDTC()
//                    .PurgeOnStartup(false)
//                 .UnicastBus()
//                    .ImpersonateSender(true)
//                 .LoadMessageHandlers()
//                 .CreateBus()
//                 .Start();
//        }
//    }
//}