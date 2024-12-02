﻿using Newtonsoft.Json;
using System;
using System.Collections;
using System.IO;
using UnityEngine;

#if UNITY_2017_4_OR_NEWER
using UnityEngine.Networking;
#endif

namespace Lachee.Discord
{
    [System.Serializable]
    public sealed class User
    {
        /// <summary>
        /// Caching Level. This is a flag.
        /// </summary>
        [System.Flags]
        public enum CacheLevelFlag
        {
            /// <summary>Disable all caching</summary>
            None = 0,

            /// <summary>Caches avatars by user id (required for caching to work).</summary>
            UserId = 1,

            /// <summary>Caches the avatars by avatar hash. </summary>
            Hash = 3, //UserId | 2,

            /// <summary>Caches the avatars by size. If off, only the largest size is stored. </summary>
            Size = 5, //UserId | 4
        }

        /// <summary>
        /// The current location of the avatar caches
        /// </summary>
        public static string CacheDirectory = null;

        /// <summary>
        /// The caching level used by the avatar functions. Note that the cache is never cleared. The cache level will help mitigate exessive file counts.
        /// <para><see cref="CacheLevelFlag.None"/> will cause no images to be cached and will be downloaded everytime they are fetched.</para>
        /// <para><see cref="CacheLevelFlag.Hash"/> will cache images based of their hash. Without this, the avatar will likely stay the same forever.</para>
        /// <para><see cref="CacheLevelFlag.Size"/> will cache images based of their size. Useful, but may result in multiples of the same file. Disabling this will cause all files to be x512.</para>
        /// </summary>
        public static CacheLevelFlag CacheLevel = CacheLevelFlag.None;

        /// <summary>
        /// The format to download and cache avatars in. By default, PNG is used.
        /// </summary>
        public static DiscordAvatarFormat AvatarFormat { get; set; }


        private DiscordRPC.User _user;

        /// <summary>
        /// The username of the Discord user
        /// </summary>
        public string username => _user?.Username;

        /// <summary>
        /// The display name of the user
        /// </summary>
        /// <remarks>This will be empty if the user has not set a global display name.</remarks>
        public string displayName => _user?.DisplayName;

        /// <summary>
        /// The discriminator of the user.
        /// </summary>
        /// <remarks>If the user has migrated to unique a <see cref="username"/>, the discriminator will always be 0.</remarks>
        [Obsolete("Discord no longer uses discriminators.")]
        public int discriminator => (_user?.Discriminator).GetValueOrDefault();

        /// <summary>
        /// The discriminator in a nicely formatted string.
        /// </summary>
        [Obsolete("Discord no longer uses discriminators.")]
        public string discrim { get { return "#" + discriminator.ToString("D4"); } }

        /// <summary>
        /// The unique snowflake ID of the Discord user
        /// </summary>
        public ulong ID => (_user?.ID).GetValueOrDefault();

        /// <summary>
        /// The hash of the users avatar. Used to generate the URL's
        /// </summary>
        public string avatarHash => (_user?.Avatar);

        /// <summary>
        /// The current avatar cache. Will return null until <see cref="GetAvatarCoroutine(DiscordAvatarSize, AvatarDownloadCallback)"/> is called.
        /// </summary>
        public Texture2D avatar { get; private set; }

        /// <summary>
        /// The size of the currently cached avatar
        /// </summary>
        public DiscordAvatarSize cacheSize { get; private set; }

        /// <summary>
        /// The format of the currently cached avatar
        /// </summary>
        public DiscordAvatarFormat cacheFormat { get; private set; }

        /// <summary>
        /// The current URL for the discord avatars
        /// </summary>
        private string cdnEndpoint => _user?.CdnEndpoint;

#if UNITY_EDITOR
#pragma warning disable 0414
        [HideInInspector]
        [SerializeField]
        private bool e_foldout = true;
#pragma warning restore 0414
#endif

        public User(DiscordRPC.User user)
        {
            _user = user;
        }

        /// <summary>
        /// An event that is triggered when the avatar finishes downloading.
        /// </summary>
        /// <param name="user">The user the avatar belongs too</param>
        /// <param name="avatar">The avatar that was downloaded</param>
        public delegate void AvatarDownloadCallback(User user, Texture2D avatar);

        /// <summary>
        /// Gets the user avatar as a Texture2D and starts it with the supplied monobehaviour. It will first check the cache if the image exists, if it does it will return the image. Otherwise it will download the image from Discord and store it in the cache, calling the callback once done.
        /// </summary>
        /// <param name="size">The target size of the avatar. Default is 128x128</param>
        /// <param name="callback">The callback for when the texture completes. Default is no-callback, but its highly recommended to use a callback</param>
        /// <returns></returns>
        public void GetAvatar(DiscordAvatarSize size = DiscordAvatarSize.x128, AvatarDownloadCallback callback = null)
        {
            DiscordManager.current.StartCoroutine(GetAvatarCoroutine(size, callback));
        }

        /// <summary>
        /// Gets the user avatar as a Texture2D as a enumerator. It will first check the cache if the image exists, if it does it will return the image. Otherwise it will download the image from Discord and store it in the cache, calling the callback once done.
        /// <para>If <see cref="CacheLevel"/> has <see cref="CacheLevelFlag.Size"/> set, then the size will be ignored and <see cref="DiscordAvatarSize.x512"/> will be used instead.</para>
        /// <para>If <see cref="CacheLevel"/> is <see cref="CacheLevelFlag.None"/>, then no files will be written for cache.</para>
        /// </summary>
        /// <param name="size">The target size of the avatar. Default is 128x128</param>
        /// <param name="callback">The callback for when the texture completes. Default is no-callback, but its highly recommended to use a callback</param>
        /// <returns></returns>
        public IEnumerator GetAvatarCoroutine(DiscordAvatarSize size = DiscordAvatarSize.x128, AvatarDownloadCallback callback = null)
        {
            if (avatar != null)
            {
                //Execute the callback (if any)
                if (callback != null)
                    callback.Invoke(this, avatar);

                //Stop here, we did all we need to do
                yield break;
            }

            if (string.IsNullOrEmpty(avatarHash))
            {
                yield return GetDefaultAvatarCoroutine(size, callback);
            }
            else
            {
                //Prepare the cache path
                string path = null;

                //Build the formatting
                if (CacheLevel != CacheLevelFlag.None)
                {
                    //Update the default cache just incase its null
                    SetupDefaultCacheDirectory();

                    string format = "{0}";
                    if ((CacheLevel & CacheLevelFlag.Hash) == CacheLevelFlag.Hash) format += "-{1}";
                    if ((CacheLevel & CacheLevelFlag.Size) == CacheLevelFlag.Size) format += "{2}"; else size = DiscordAvatarSize.x512;

                    //Generate the path name
                    string filename = string.Format(format + ".{3}", ID, avatarHash, size.ToString(), User.AvatarFormat.ToString().ToLowerInvariant());
                    path = Path.Combine(CacheDirectory, filename);
                    Debug.Log("<color=#FA0B0F>Cache:</color> " + path);
                }

                //The holder texture is null, so we should create new one
                Texture2D avatarTexture = new Texture2D((int)size, (int)size, TextureFormat.RGBA32, false);

                //Check if the file exists and we have caching enabled
                if (CacheLevel != CacheLevelFlag.None && File.Exists(path))
                {
                    //Load the image
                    var bytes = File.ReadAllBytes(path);
                    avatarTexture.LoadImage(bytes);
                }
                else
                {
#if UNITY_2017_4_OR_NEWER
                    using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(GetAvatarURL(User.AvatarFormat, size)))
                    {
                        //Download the texture
                        yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                    if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
                        if (req.isNetworkError || req.isHttpError)
#endif
                        {
                            //Report errors
                            Debug.LogError("Failed to download user avatar: " + req.error);
                        }
                        else
                        {
                            //Update the avatar
                            avatarTexture = DownloadHandlerTexture.GetContent(req);
                        }
                    }
#else
                using (WWW www = new WWW(GetAvatarURL(User.AvatarFormat, size)))
                {
                    //Download the texture
                    yield return www;

                    //Update the holder
                    www.LoadImageIntoTexture(avatarTexture);
                }
#endif
                }

                //Apply our avatar and update our cache
                if (avatarTexture != null)
                {
                    CacheAvatarTexture(avatarTexture, path);
                    avatar = avatarTexture;
                    cacheFormat = User.AvatarFormat;
                    cacheSize = size;

                    //Execute the callback (if any)
                    if (callback != null)
                        callback.Invoke(this, avatarTexture);
                }
            }
        }

        /// <summary>Caches the avatar</summary>
        private void CacheAvatarTexture(Texture2D texture, string path)
        {
            //Encode and cache the files
            if (CacheLevel != CacheLevelFlag.None)
            {
                //Create the directory if it doesnt already exist
                if (!Directory.Exists(CacheDirectory))
                    Directory.CreateDirectory(CacheDirectory);

                //Encode the image
                byte[] bytes;
                switch (User.AvatarFormat)
                {
                    default:
                    case DiscordAvatarFormat.PNG:
                        bytes = texture.EncodeToPNG();
                        break;

                    case DiscordAvatarFormat.JPEG:
                        bytes = texture.EncodeToJPG();
                        break;
                }

                //Save the image
                File.WriteAllBytes(path, bytes);
            }
        }


        /// <summary>
        /// Gets the default avatar for the given user. Will check the cache first, and if none are available it will then download the default from discord.	
        /// <para>If <see cref="CacheLevel"/> has <see cref="CacheLevelFlag.Size"/> set, then the size will be ignored and <see cref="DiscordAvatarSize.x512"/> will be used instead.</para>
        /// <para>If <see cref="CacheLevel"/> is <see cref="CacheLevelFlag.None"/>, then no files will be written for cache.</para>
        /// </summary>
        /// <param name="size">The size of the target avatar</param>
        /// <param name="callback">The callback that will be made when the picture finishes downloading.</param>
        /// 
        /// <returns></returns>
        public IEnumerator GetDefaultAvatarCoroutine(DiscordAvatarSize size = DiscordAvatarSize.x128, AvatarDownloadCallback callback = null)
        {
            //Calculate the discrim number and prepare the cache path            
            string path = null;
            int index = (int)((ID >> 22) % 6);

#pragma warning disable CS0618 // Disable the obsolete warning as we know the discriminator is obsolete and we are validating it here.
            if (discriminator > 0)
                index = discriminator % 5;
#pragma warning restore CS0618

            //Update the default cache just incase its null
            if (CacheLevel != CacheLevelFlag.None)
            {
                //Setup the dir
                SetupDefaultCacheDirectory();

                //should we cache the size?
                bool cacheSize = (CacheLevel & CacheLevelFlag.Size) == CacheLevelFlag.Size;
                if (!cacheSize) size = DiscordAvatarSize.x512;

                string filename = string.Format("default-{0}{1}.png", index, cacheSize ? size.ToString() : "");
                path = Path.Combine(CacheDirectory, filename);
            }

            //The holder texture is null, so we should create new one
            Texture2D avatarTexture = new Texture2D((int)size, (int)size, TextureFormat.RGBA32, false);

            //Check if the file exists
            if (CacheLevel != CacheLevelFlag.None && File.Exists(path))
            {
                //Load the image
                byte[] bytes = File.ReadAllBytes(path);
                avatarTexture.LoadImage(bytes);
            }
            else
            {
                string url = string.Format("https://{0}/embed/avatars/{1}.png?size={2}", cdnEndpoint, index, (int)size);

#if UNITY_2017_4_OR_NEWER
                using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
                {
                    //Download the texture
                    yield return req.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
                if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
                    if (req.isNetworkError || req.isHttpError)
#endif
                    {
                        //Report errors
                        Debug.LogError("Failed to download default avatar: " + req.error);
                    }
                    else
                    {
                        //Update the avatar
                        avatarTexture = DownloadHandlerTexture.GetContent(req);
                    }
                }
#else
            using (WWW www = new WWW(url))
            {
                //Download the texture
                yield return www;

                //Update the holder
                www.LoadImageIntoTexture(avatarTexture);
            }
#endif
                //We have been told to cache, so do so.
                if (CacheLevel != CacheLevelFlag.None)
                {
                    //Create the directory if it doesnt already exist
                    if (!Directory.Exists(CacheDirectory))
                        Directory.CreateDirectory(CacheDirectory);

                    byte[] bytes = avatarTexture.EncodeToPNG();
                    File.WriteAllBytes(path, bytes);
                }
            }

            //Apply our avatar and update our cache
            avatar = avatarTexture;
            cacheFormat = DiscordAvatarFormat.PNG;
            cacheSize = size;

            //Execute the callback (if any)
            if (callback != null)
                callback.Invoke(this, avatar);
        }

        /// <summary>
        /// Updates the default directory for the cache
        /// </summary>
        private static void SetupDefaultCacheDirectory()
        {
            if (CacheDirectory == null)
                CacheDirectory = Application.dataPath + "/Discord Rpc/Cache";
        }
       
        /// <summary>
        /// Gets a URL that can be used to download the user's avatar. If the user has not yet set their avatar, it will return the default one that discord is using. The default avatar only supports the <see cref="AvatarFormat.PNG"/> format.
        /// </summary>
        /// <param name="format">The format of the target avatar</param>
        /// <param name="size">The optional size of the avatar you wish for.</param>
        /// <returns>URL to the discord CDN for the particular avatar</returns>
        private string GetAvatarURL(DiscordAvatarFormat format, DiscordAvatarSize size)
        {
            return _user.GetAvatarURL(format == DiscordAvatarFormat.PNG ? DiscordRPC.User.AvatarFormat.PNG : DiscordRPC.User.AvatarFormat.JPEG, (DiscordRPC.User.AvatarSize)size);
        }

        /// <summary>
        /// Formats the user into a displayable format. If the user has a <see cref="displayName"/>, then this will be used.
        /// <para>If the user still has a discriminator, then this will return the form of `Username#Discriminator`.</para>
        /// </summary>
        /// <returns>String of the user that can be used for display.</returns>
        public override string ToString()
        {
            if (_user == null) return "N/A";
            return _user.ToString();
        }

        /// <summary>
        /// Implicit casting from a DiscordRPC.User to a DiscordUser
        /// </summary>
        /// <param name="user"></param>
        public static implicit operator User(DiscordRPC.User user) { return new User(user); }
        public static implicit operator DiscordRPC.User(User user) { return user._user; }

        public override int GetHashCode()
        {
            return this.ID.GetHashCode() ^ 7;
        }

        public override bool Equals(object obj)
        {
            if (obj is User)
                return this.ID == ((User)obj).ID;

            return false;
        }
    }

    /// <summary>
    /// The format of the discord avatars in the cache
    /// </summary>
    public enum DiscordAvatarFormat
    {
        /// <summary>
        /// Portable Network Graphics format (.png)
        /// <para>Losses format that supports transparent avatars. Most recommended for stationary formats with wide support from many libraries.</para>
        /// </summary>
        PNG,

        /// <summary>
        /// Joint Photographic Experts Group format (.jpeg)
        /// <para>The format most cameras use. Lossy and does not support transparent avatars.</para>
        /// </summary>
        JPEG
    }

    /// <summary>
    /// Possible square sizes of avatars.
    /// </summary>
    public enum DiscordAvatarSize
    {
        /// <summary> 16 x 16 pixels.</summary>
        x16 = 16,
        /// <summary> 32 x 32 pixels.</summary>
        x32 = 32,
        /// <summary> 64 x 64 pixels.</summary>
        x64 = 64,
        /// <summary> 128 x 128 pixels.</summary>
        x128 = 128,
        /// <summary> 256 x 256 pixels.</summary>
        x256 = 256,
        /// <summary> 512 x 512 pixels.</summary>
        x512 = 512,
        /// <summary> 1024 x 1024 pixels.</summary>
        x1024 = 1024,
        /// <summary> 2048 x 2048 pixels.</summary>
        x2048 = 2048
    }

    /// <summary>
    /// Collection of extensions to the <see cref="DiscordRPC.User"/> class.
    /// </summary>
    public static class DiscordUserExtension
    {
        /// <summary>
        /// Gets the user avatar as a Texture2D and starts it with the supplied monobehaviour. It will first check the cache if the image exists, if it does it will return the image. Otherwise it will download the image from Discord and store it in the cache, calling the callback once done.
        /// <para>An alias of <see cref="User.CacheAvatar(MonoBehaviour, DiscordAvatarSize, AvatarDownloadCallback)"/> and will return the new <see cref="User"/> instance.</para>
        /// </summary>
        /// <param name="size">The target size of the avatar. Default is 128x128</param>
        /// <param name="callback">The callback for when the texture completes. Default is no-callback, but its highly recommended to use a callback</param>
        /// <returns>Returns the generated <see cref="User"/> for this <see cref="DiscordRPC.User"/> object.</returns>
        public static User GetAvatar(this DiscordRPC.User user,DiscordAvatarSize size = DiscordAvatarSize.x128, User.AvatarDownloadCallback callback = null)
        {
            var du = new User(user);
            du.GetAvatar(size, callback);
            return du;
        }

        /// <summary>
        /// Gets the user avatar as a Texture2D as a enumerator. It will first check the cache if the image exists, if it does it will return the image. Otherwise it will download the image from Discord and store it in the cache, calling the callback once done.
        /// <para>An alias of <see cref="User.CacheAvatarCoroutine(DiscordAvatarSize, User.AvatarDownloadCallback)"/> and will return the new <see cref="User"/> instance in the callback.</para>
        /// </summary>
        /// <param name="size">The target size of the avatar. Default is 128x128</param>
        /// <param name="callback">The callback for when the texture completes. Default is no-callback, but its highly recommended to use a callback</param>
        /// <returns></returns>
        public static IEnumerator GetAvatarCoroutine(this DiscordRPC.User user, DiscordAvatarSize size = DiscordAvatarSize.x128, User.AvatarDownloadCallback callback = null)
        {
            var du = new User(user);
            return du.GetDefaultAvatarCoroutine(size, callback);
        }

        /// <summary>
        /// Gets the default avatar for the given user. Will check the cache first, and if none are available it will then download the default from discord.
        /// <para>An alias of <see cref="User.CacheDefaultAvatarCoroutine(DiscordAvatarSize, User.AvatarDownloadCallback)"/> and will return the new <see cref="User"/> instance in the callback.</para>
        /// </summary>
        /// <param name="size">The size of the target avatar</param>
        /// <param name="callback">The callback that will be made when the picture finishes downloading.</param>
        /// <returns></returns>
        public static IEnumerator GetDefaultAvatarCoroutine(this DiscordRPC.User user, DiscordAvatarSize size = DiscordAvatarSize.x128, User.AvatarDownloadCallback callback = null)
        {
            var du = new User(user);
            return du.GetDefaultAvatarCoroutine(size, callback);
        }
    }
}
