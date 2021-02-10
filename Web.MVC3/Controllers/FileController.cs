using System.Net.Mime;
using System.Web;
using System.Web.Mvc;
using System.Web.UI;
using Framework.Drawing;
using Framework.Infrastructure;
using Services.ModelServices;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

namespace Web.Controllers
{
    public partial class FileController : Controller
    {
        protected byte[] EmptyUserThumb
        {
            get
            {
                if (HttpContext.Application["EmptyUserThumb"] == null)
                {
                    var file = System.IO.File.ReadAllBytes(Server.MapPath(Links.Content.Images.nophoto_avatar_gif));
                    HttpContext.Application["EmptyUserThumb"] = PictureProcessor.ResizeImageFile(file, 50);
                }

                return (byte[])HttpContext.Application["EmptyUserThumb"];
            }
        }

        public virtual ActionResult GetProfilePicture(string userObjectId)
        {
            var service = ServiceLocator.Resolve<UserService>();
            var image = service.GetProfilePicture(userObjectId);
            if (image == null)
            {
                return new EmptyResult();
            }

            return File(image.File, image.ContentType);
        }

        public virtual ActionResult GetProfilePictureThumb(string userObjectId)
        {
            var service = ServiceLocator.Resolve<UserService>();
            var image = service.GetProfilePictureThumb(userObjectId);
            if (image == null)
            {
                return File(EmptyUserThumb, MediaTypeNames.Image.Jpeg);
            }

            return File(image.File, image.ContentType);
        }

        public virtual ActionResult GetOrganizationLogo(string objectId)
        {
            var service = ServiceLocator.Resolve<OrganizationService>();
            var image = service.GetProfilePicture(objectId);
            if (image == null)
            {
                return new EmptyResult();
            }

            return File(image.File, image.ContentType);
        }

        public virtual ActionResult GetOrganizationLogoThumb(string objectId)
        {
            var service = ServiceLocator.Resolve<OrganizationService>();
            var image = service.GetProfilePictureThumb(objectId);
            if (image == null)
            {
                return File(EmptyUserThumb, MediaTypeNames.Image.Jpeg);
            }

            return File(image.File, image.ContentType);
        }

        public virtual ActionResult GetEmptyThumb()
        {
            return File(EmptyUserThumb, MediaTypeNames.Image.Jpeg);
        }

        [OutputCache(Duration = int.MaxValue, Location = OutputCacheLocation.ServerAndClient, VaryByParam = "id")]
        public virtual ActionResult GetUserPicture(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return File(EmptyUserThumb, MediaTypeNames.Image.Jpeg);
            }
            var service = ServiceLocator.Resolve<UserService>();
            var image = service.GetPicture(id);
            if (image == null)
            {
                return File(EmptyUserThumb, MediaTypeNames.Image.Jpeg);
            }

            return File(image.File, image.ContentType);
        }

        [OutputCache(Duration = int.MaxValue, Location = OutputCacheLocation.ServerAndClient, VaryByParam = "id")]
        public virtual ActionResult GetOrganizationPicture(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return File(EmptyUserThumb, MediaTypeNames.Image.Jpeg);
            }

            var service = ServiceLocator.Resolve<OrganizationService>();
            var image = service.GetPicture(id);
            if (image == null)
            {
                return File(EmptyUserThumb, MediaTypeNames.Image.Jpeg);
            }

            return File(image.File, image.ContentType);
        }
    }
}
