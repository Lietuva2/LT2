using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.ViewModels.Organization;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Storage;
using Framework.Strings;
using MongoDB.Bson;
using MongoDB.Driver.Builders;

using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class OrganizationProjectCommandHandler : ActionCommandHandler<OrganizationProjectCommand>
    {
        public readonly NotificationService notificationService;
        public readonly ProjectNotification notification;

        public OrganizationProjectCommandHandler(NotificationService notificationService, ProjectNotification notification)
        {
            this.notificationService = notificationService;
            this.notification = notification;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(OrganizationProjectCommand command)
        {
            Service.ProcessOrganizationProjectCommand(command, command.ProjectId, command.ToDoId, command.ToDoLink, command.CommentId);

            if (command.SendNotifications)
            {
                notificationService.SendOrganizationProjectNotification(notification, command);
            }
        }
    }
}
