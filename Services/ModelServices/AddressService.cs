using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Data.EF.Actions;
using Data.EF.Users;
using Data.Infrastructure.Sessions;
using Data.ViewModels.Account;
using EntityFramework.Caching;
using EntityFramework.Extensions;
using Framework;
using Framework.Bus;
using Framework.Lists;
using Framework.Mvc.Lists;
using Framework.Strings;
using Globalization.Resources.Services;

using Services.Caching;
using Services.Infrastructure;
using Services.Session;

namespace Services.ModelServices
{
    public class AddressService : IService
    {
        private IUsersContextFactory usersSessionFactory;
        private Func<INoSqlSession> noSqlSessionFactory;
        private IActionsContextFactory actionSessionFactory;

        private readonly IBus bus;

        private int ItemsCount { get { return CustomAppSettings.AutocompleteItemsCount; } }

        public UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }

        public AddressService(
            IUsersContextFactory usersSessionFactory,
            Func<INoSqlSession> mongoDbSessionFactory,
            IActionsContextFactory actionSessionFactory,
            IBus bus)
        {
            this.usersSessionFactory = usersSessionFactory;
            this.noSqlSessionFactory = mongoDbSessionFactory;
            this.actionSessionFactory = actionSessionFactory;
            this.bus = bus;
        }

        public IList<string> GetCountries(string prefix)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                return session.Countries.Where(c => c.Name.StartsWith(prefix)).OrderBy(c => c.Name).Take(ItemsCount).Select(c => c.Name).ToList();
            }
        }

        public int GetCountryId(string countryName)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                return session.Countries.Where(c => c.Name == countryName).Select(c => c.Id).SingleOrDefault();
            }
        }

        public IList<LabelValue> GetCities(string country, string municipality, string prefix)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                var query = from c in session.Cities
                            where c.Name.StartsWith(prefix)
                            select c;
                if (!string.IsNullOrEmpty(municipality))
                {
                    query = from c in query
                            where c.Municipality.Name == municipality
                            select c;
                }

                if (!string.IsNullOrEmpty(country))
                {
                    query = from c in query
                            where c.Municipality.Country.Name == country
                            select c;
                }

                return (from q in query
                        select new LabelValue() { label = q.Name + ", " + q.Municipality.Name + ", " + q.Municipality.Country.Name, value = q.Name }).ToList();
            }
        }

        public IList<SelectListItem> GetCities(int countryId)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                var query = from c in session.Cities
                            where c.Municipality.CountryId == countryId
                            orderby c.Name
                            select c;

                var result = (from q in query
                              select new TextValue() { Text = q.Name, ValueInt = q.Id }).ToList().ToSelectList(false).ToList();

                result.Insert(0, new SelectListItem()
                                     {
                                         Selected = true,
                                         Text = CommonStrings.LT,
                                         Value = "0"
                                     });
                return result;
            }
        }

        public string GetCityName(int? cityId)
        {
            if(!cityId.HasValue)
            {
                return CommonStrings.LT;
            }
            using (var session = usersSessionFactory.CreateContext())
            {
                return session.Cities.Single(c => c.Id == cityId).Name;
            }
        }

        public AdditionalUniqueInfoModel GetAdditionalUniqueInfoModel(int? cityId)
        {
            if (!cityId.HasValue)
            {
                return null;
            }
            using (var session = usersSessionFactory.CreateContext())
            {
                return session.Cities.Where(c => c.Id == cityId).Select(c => new AdditionalUniqueInfoModel()
                                                                                         {
                                                                                             City = c.Name,
                                                                                             Municipality = c.Municipality.Name,
                                                                                             Country = c.Municipality.Country.Name,
                                                                                             CityId = cityId
                                                                                         }).SingleOrDefault();
            }
        }

        public int? GetCityId(string cityName, int municipalityId)
        {
            if(string.IsNullOrEmpty(cityName))
            {
                return null;
            }
            using (var session = usersSessionFactory.CreateContext())
            {
                var city = session.Cities.SingleOrDefault(c => c.Name == cityName && c.MunicipalityId == municipalityId);
                if(city != null)
                {
                    return city.Id;
                }
            }

            return null;
        }

        public int? GetCityId(string cityName, string municipalityName, string countryName)
        {
            if (string.IsNullOrEmpty(cityName) || string.IsNullOrEmpty(municipalityName) || string.IsNullOrEmpty(countryName))
            {
                return null;
            }

            var municipalityId = GetMunicipalityId(municipalityName, countryName);
            if(!municipalityId.HasValue)
            {
                return null;
            }

            return GetCityId(cityName, municipalityId.Value);
        }

        public IList<SelectListItem> GetMunicipalities(int countryId)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                var query = from c in session.Municipalities
                            where c.CountryId == countryId
                            orderby c.Name
                            select c;

                var result = (from q in query
                              select new TextValue() { Text = q.Name, ValueInt = q.Id }).FromCache().ToSelectList(false).ToList();

                result.Insert(0, new SelectListItem()
                {
                    Selected = true,
                    Text = CommonStrings.LT,
                    Value = "0"
                });

                return result;
            }
        }

        public string GetMunicipality(int? municipalityId)
        {
            if (!municipalityId.HasValue)
            {
                return null;
            }
            using (var session = usersSessionFactory.CreateContext())
            {
                return session.Municipalities.Single(c => c.Id == municipalityId).Name;
            }
        }

        public int? GetMunicipalityId(string name, string country)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            using (var session = usersSessionFactory.CreateContext())
            {
                var mun = session.Municipalities.SingleOrDefault(c => c.Name == name && c.Country.Name == country);
                if (mun != null)
                {
                    return mun.Id;
                }
            }

            return null;
        }

        public IList<LabelValue> GetMunicipalities(string country, string prefix)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                var query = from c in session.Municipalities
                            where c.Name.StartsWith(prefix)
                            select c;

                if (!string.IsNullOrEmpty(country))
                {
                    query = from c in query
                            where c.Country.Name == country
                            select c;
                }
                return (from q in query
                        select new LabelValue { label = q.Name + ", " + q.Country.Name, value = q.Name }).ToList();
            }
        }

        public List<int> GetUserMunicipalities(string userId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                var user = noSqlSession.GetById<Data.MongoDB.User>(userId);
                var list = new List<int>();
                if (user.MunicipalityId.HasValue)
                {
                    list.Add(user.MunicipalityId.Value);
                }
                else
                {
                    var id = GetMunicipalityId(user.Municipality, user.Country);
                    if (id.HasValue)
                    {
                        list.Add(id.Value);
                        user.MunicipalityId = id.Value;
                    }
                }

                if (user.OriginMunicipalityId.HasValue)
                {
                    list.Add(user.OriginMunicipalityId.Value);
                }
                else
                {
                    var id = GetMunicipalityId(user.OriginMunicipality, user.OriginCountry);
                    if (id.HasValue)
                    {
                        list.Add(id.Value);
                        user.OriginMunicipalityId = id.Value;
                    }
                }

                noSqlSession.Update(user);

                return list;
            }
        }

        /// <summary>
        /// Saves the address.
        /// </summary>
        /// <param name="country"></param>
        /// <param name="municipality"></param>
        /// <param name="city"></param>
        /// <returns>The city id</returns>
        public int? SaveAddress(string country, string municipality, string city)
        {
            country = country.ToTitleCase();
            municipality = municipality.ToTitleCase();
            city = city.ToTitleCase();

            City cty = null;
            using (var session = usersSessionFactory.CreateContext(true))
            {
                var cntry = session.Countries.SingleOrDefault(c => c.Name == country);
                if (cntry == null)
                {
                    cntry = new Country
                                {
                                    Name = country
                                };
                    session.Countries.Add(cntry);
                }
                if (!string.IsNullOrEmpty(municipality))
                {
                    Municipality mnc = null;
                    if (cntry.Id > 0)
                    {
                        mnc = session.Municipalities.SingleOrDefault(m => m.CountryId == cntry.Id && m.Name == municipality);
                    }
                    if (mnc == null)
                    {
                        mnc = new Municipality()
                                  {
                                      Name = municipality
                                  };
                        cntry.Municipalities.Add(mnc);
                    }

                    if (!string.IsNullOrEmpty(city))
                    {
                        if (mnc.Id > 0)
                        {
                            cty = session.Cities.SingleOrDefault(c => c.MunicipalityId == mnc.Id && c.Name == city);
                        }
                        if (cty == null)
                        {
                            cty = new City
                                      {
                                          Name = city
                                      };
                            mnc.Cities.Add(cty);
                        }
                    }
                }
                CacheManager.Current.Expire("municipalities" + cntry.Id);
            }
            if(cty == null)
            {
                return null;
            }

            return cty.Id;
        }

        public void SetMunicipality(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                WorkContext.Municipality = null;
                return;
            }

            using(var session = usersSessionFactory.CreateContext())
            {
                var mun = session.Municipalities.SingleOrDefault(m => m.ShortName == name);
                if (mun == null)
                {
                    WorkContext.Municipality = null;
                }
                else
                {
                    WorkContext.Municipality = new MunicipalityInfo()
                                                           {
                                                               Id = mun.Id,
                                                               Name = mun.Name
                                                           };
                }
            }
        }

        public List<MunicipalityInfo> GetAllMunicipalities()
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                return session.Municipalities.Where(m => m.ShortName != null).Select(m => new MunicipalityInfo()
                                                                                                                       {
                                                                                                                           Id = m.Id,
                                                                                                                           Name = m.Name,
                                                                                                                           ShortName = m.ShortName
                                                                                                                       }).ToList();
            }
        }

        public bool ValidateMunicipality(string country, string municipality)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                var dbcountry = session.Countries.SingleOrDefault(c => c.Name == country);
                if (dbcountry != null && dbcountry.Id == 1)
                {
                    return dbcountry.Municipalities.Any(m => m.Name == municipality);
                }
            }

            return true;
        }
    }
}
