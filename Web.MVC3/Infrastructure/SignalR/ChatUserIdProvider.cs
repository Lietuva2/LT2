using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNet.SignalR;
using Services.ModelServices;

namespace Web.Infrastructure.SignalR
{
    public class ChatUserIdProvider : IUserIdProvider 
    {
        private readonly UserService userService;
        public ChatUserIdProvider(UserService userService)
        {
            this.userService = userService;
        }
        public string GetUserId(IRequest request)
        {
            var id = userService.GetUserObjectId(request.User.Identity.Name);
            return id;
        }
    }
}