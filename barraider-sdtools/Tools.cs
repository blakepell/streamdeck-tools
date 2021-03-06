﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BarRaider.SdTools
{
    /// <summary>
    /// Set of common utilities used by various plugins
    /// Currently the class mostly focuses on image-related functions that will be passed to the StreamDeck key
    /// </summary>
    public static class Tools
    {
        private const string HEADER_PREFIX = "data:image/png;base64,";

        /// <summary>
        /// Default height, in pixels, on a key
        /// </summary>
        public const int KEY_DEFAULT_HEIGHT = 72;

        /// <summary>
        /// Default width, in pixels, on a key
        /// </summary>
        public const int KEY_DEFAULT_WIDTH = 72;

        #region Image Related

        /// <summary>
        /// Convert an image file to Base64 format. Set the addHeaderPrefix to true, if this is sent to the SendImageAsync function
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="addHeaderPrefix"></param>
        /// <returns></returns>
        public static string FileToBase64(string fileName, bool addHeaderPrefix)
        {
            if (!File.Exists(fileName))
            {
                return null;
            }

            using (Image image = Image.FromFile(fileName))
            {
                return ImageToBase64(image, addHeaderPrefix);
            }
        }

        /// <summary>
        /// Convert a in-memory image object to Base64 format. Set the addHeaderPrefix to true, if this is sent to the SendImageAsync function
        /// </summary>
        /// <param name="image"></param>
        /// <param name="addHeaderPrefix"></param>
        /// <returns></returns>
        public static string ImageToBase64(Image image, bool addHeaderPrefix)
        {
            using (MemoryStream m = new MemoryStream())
            {
                image.Save(m, ImageFormat.Png);
                byte[] imageBytes = m.ToArray();

                // Convert byte[] to Base64 String
                string base64String = Convert.ToBase64String(imageBytes);
                return addHeaderPrefix ? HEADER_PREFIX + base64String : base64String;
            }
        }

        /// <summary>
        /// Convert a base64 image string to an Image object
        /// </summary>
        /// <param name="base64String"></param>
        /// <returns></returns>
        public static Image Base64StringToImage(string base64String)
        {
            try
            {
                // Remove header
                if (base64String.Substring(0, HEADER_PREFIX.Length) == HEADER_PREFIX)
                {
                    base64String = base64String.Substring(HEADER_PREFIX.Length);
                }

                byte[] imageBytes = Convert.FromBase64String(base64String);
                using (MemoryStream m = new MemoryStream(imageBytes))
                {
                    return Image.FromStream(m);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Base64StringToImage Exception: {ex}");
            }
            return null;
        }

        /// <summary>
        /// Generates an empty key bitmap with the default height and width
        /// </summary>
        /// <param name="graphics"></param>
        /// <returns></returns>
        public static Bitmap GenerateKeyImage(out Graphics graphics)
        {
            Bitmap bitmap = new Bitmap(KEY_DEFAULT_WIDTH, KEY_DEFAULT_HEIGHT);
            var brush = new SolidBrush(Color.Black);

            graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            //Fill background black
            graphics.FillRectangle(brush, 0, 0, KEY_DEFAULT_WIDTH, KEY_DEFAULT_HEIGHT);
            return bitmap;
        }

        /// <summary>
        /// Extracts the actual filename from a file payload received from the Property Inspector
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public static string FilenameFromPayload(Newtonsoft.Json.Linq.JToken payload)
        {
            return FilenameFromString((string)payload);
        }

        private static string FilenameFromString(string filenameWithFakepath)
        {
            return Uri.UnescapeDataString(filenameWithFakepath.Replace("C:\\fakepath\\", ""));
        }

        #endregion

        #region JObject Related

        /// <summary>
        /// Itterates through the fromJObject, finds the propery that matches in the toSettings object, and sets the value from the fromJObject object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toSettings"></param>
        /// <param name="fromJObject"></param>
        /// <returns>Number of properties updated</returns>
        public static int AutoPopulateSettings<T>(T toSettings, JObject fromJObject)
        {
            Dictionary<string, PropertyInfo> dicProperties = MatchPropertiesWithJsonProperty(toSettings);
            int totalPopulated = 0;

            if (fromJObject != null)
            {
                foreach (var prop in fromJObject)
                {
                    if (dicProperties.ContainsKey(prop.Key))
                    {
                        PropertyInfo info = dicProperties[prop.Key];

                        // Special handling for FilenameProperty
                        if (info.GetCustomAttributes(typeof(FilenamePropertyAttribute), true).Length > 0)
                        {
                            string value = FilenameFromString((string)prop.Value);
                            info.SetValue(toSettings, value);
                        }
                        else
                        {
                            info.SetValue(toSettings, Convert.ChangeType(prop.Value, info.PropertyType));
                        }
                        totalPopulated++;
                    }
                }
            }
            return totalPopulated;
        }

        private static Dictionary<string, PropertyInfo> MatchPropertiesWithJsonProperty<T>(T obj)
        {
            Dictionary<string, PropertyInfo> dicProperties = new Dictionary<string, PropertyInfo>();
            if (obj != null)
            {
                PropertyInfo[] props = typeof(T).GetProperties();
                foreach (PropertyInfo prop in props)
                {
                    object[] attributes = prop.GetCustomAttributes(true);
                    foreach (object attr in attributes)
                    {
                        JsonPropertyAttribute jprop = attr as JsonPropertyAttribute;
                        if (jprop != null)
                        {
                            dicProperties.Add(jprop.PropertyName, prop);
                            break;
                        }
                    }
                }
            }

            return dicProperties;
        }

        #endregion

        #region Plugin Helper Classes

        /// <summary>
        /// Uses the PluginActionId attribute on the various classes derived from PluginBase to find all the actions supported in this assembly
        /// </summary>
        /// <returns></returns>
        public static PluginActionId[] AutoLoadPluginActions()
        {
            List<PluginActionId> actions = new List<PluginActionId>();

            var pluginTypes = Assembly.GetEntryAssembly().GetTypes().Where(typ => typ.IsClass && typ.GetCustomAttributes(typeof(PluginActionIdAttribute), true).Length > 0).ToList();
            pluginTypes.ForEach(typ =>
            {
                var attr = typ.GetCustomAttributes(typeof(PluginActionIdAttribute), true).First() as PluginActionIdAttribute;
                if (attr != null)
                {
                    actions.Add(new PluginActionId(attr.ActionId, typ));
                }
            });

            return actions.ToArray();
        }

        #endregion
    }
}
