﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Umbraco.Core.Cache;
using Umbraco.Core.IO;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.Services;

namespace Umbraco.Core.Models
{
    public static class UserExtensions
    {
        /// <summary>
        /// Returns all of the user's assigned start node ids based on ids assigned directly to the IUser object and it's groups
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<int> GetCombinedStartContentIds(this IUser user)
        {
            return user.StartContentIds.Concat(user.Groups.Select(x => x.StartContentId)).Distinct();
        }

        /// <summary>
        /// Returns all of the user's assigned start node ids based on ids assigned directly to the IUser object and it's groups
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<int> GetCombinedStartMediaIds(this IUser user)
        {
            return user.StartMediaIds.Concat(user.Groups.Select(x => x.StartMediaId)).Distinct();
        }

        /// <summary>
        /// Tries to lookup the user's gravatar to see if the endpoint can be reached, if so it returns the valid URL
        /// </summary>
        /// <param name="user"></param>
        /// <param name="userService"></param>
        /// <param name="staticCache"></param>
        /// <returns>
        /// A list of 5 different sized avatar URLs
        /// </returns>
        internal static string[] GetCurrentUserAvatarUrls(this IUser user, IUserService userService, ICacheProvider staticCache)
        {
            if (user.Avatar.IsNullOrWhiteSpace())
            {
                var gravatarHash = user.Email.ToMd5();
                var gravatarUrl = "https://www.gravatar.com/avatar/" + gravatarHash + "?d=404";

                //try gravatar
                var gravatarAccess = staticCache.GetCacheItem<bool>("UserAvatar" + user.Id, () =>
                {
                    // Test if we can reach this URL, will fail when there's network or firewall errors
                    var request = (HttpWebRequest)WebRequest.Create(gravatarUrl);
                    // Require response within 10 seconds
                    request.Timeout = 10000;
                    try
                    {
                        using ((HttpWebResponse)request.GetResponse()) { }
                    }
                    catch (Exception)
                    {
                        // There was an HTTP or other error, return an null instead
                        return false;
                    }
                    return true;
                });

                if (gravatarAccess)
                {
                    return new[]
                    {
                        gravatarUrl  + "&s=30",
                        gravatarUrl  + "&s=60",
                        gravatarUrl  + "&s=90",
                        gravatarUrl  + "&s=150",
                        gravatarUrl  + "&s=300"
                    };
                }

                return null;
            }

            //use the custom avatar
            var avatarUrl = FileSystemProviderManager.Current.MediaFileSystem.GetUrl(user.Avatar);
            return new[]
            {
                avatarUrl  + "?width=30&height=30&mode=crop",
                avatarUrl  + "?width=60&height=60&mode=crop",
                avatarUrl  + "?width=90&height=90&mode=crop",
                avatarUrl  + "?width=150&height=150&mode=crop",
                avatarUrl  + "?width=300&height=300&mode=crop"
            };

        }

        /// <summary>
        /// Returns the culture info associated with this user, based on the language they're assigned to in the back office
        /// </summary>
        /// <param name="user"></param>
        /// <param name="textService"></param>
        /// <returns></returns>      
        public static CultureInfo GetUserCulture(this IUser user, ILocalizedTextService textService)
        {
            if (user == null) throw new ArgumentNullException("user");
            if (textService == null) throw new ArgumentNullException("textService");
            return GetUserCulture(user.Language, textService);
        }

        internal static CultureInfo GetUserCulture(string userLanguage, ILocalizedTextService textService)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(userLanguage.Replace("_", "-"));
                //TODO: This is a hack because we store the user language as 2 chars instead of the full culture 
                // which is actually stored in the language files (which are also named with 2 chars!) so we need to attempt
                // to convert to a supported full culture
                var result = textService.ConvertToSupportedCultureWithRegionCode(culture);
                return result;
            }
            catch (CultureNotFoundException)
            {
                //return the default one
                return CultureInfo.GetCultureInfo("en");
            }
        }

        /// <summary>
        /// Checks if the user has access to the content item based on their start noe
        /// </summary>
        /// <param name="user"></param>
        /// <param name="content"></param>
        /// <returns></returns>
        internal static bool HasPathAccess(this IUser user, IContent content)
        {
            if (user == null) throw new ArgumentNullException("user");
            if (content == null) throw new ArgumentNullException("content");
            return HasPathAccess(content.Path, user.GetCombinedStartContentIds().ToArray(), Constants.System.RecycleBinContent);
        }
        
        internal static bool HasPathAccess(string path, int[] startNodeIds, int recycleBinId)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Value cannot be null or whitespace.", "path");

            var formattedPath = "," + path + ",";
            var formattedRecycleBinId = "," + recycleBinId.ToInvariantString() + ",";

            //check for root path access
            //TODO: This logic may change
            if (startNodeIds.Length == 0 || startNodeIds.Contains(Constants.System.Root))
                return true;

            //only users with root access have access to the recycle bin so if the above check didn't pass than access is denied
            if (formattedPath.Contains(formattedRecycleBinId))
            {
                return false;
            }            

            //check for normal paths
            foreach (var startNodeId in startNodeIds)
            {                
                var formattedStartNodeId = "," + startNodeId.ToInvariantString() + ",";

                var hasAccess = formattedPath.Contains(formattedStartNodeId);
                if (hasAccess)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if the user has access to the media item based on their start noe
        /// </summary>
        /// <param name="user"></param>
        /// <param name="media"></param>
        /// <returns></returns>
        internal static bool HasPathAccess(this IUser user, IMedia media)
        {
            if (user == null) throw new ArgumentNullException("user");
            if (media == null) throw new ArgumentNullException("media");
            return HasPathAccess(media.Path, user.GetCombinedStartMediaIds().ToArray(), Constants.System.RecycleBinMedia);
        }
        
        /// <summary>
        /// Determines whether this user is an admin.
        /// </summary>
        /// <param name="user"></param>
        /// <returns>
        /// 	<c>true</c> if this user is admin; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsAdmin(this IUser user)
        {
            if (user == null) throw new ArgumentNullException("user");
            return user.Groups != null && user.Groups.Any(x => x.Alias == Constants.Security.AdminGroupAlias);
        }
    }
}