using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Activities;
using System.Text.RegularExpressions;
using Microsoft.TeamFoundation.Build.Client;
using System.Collections.Generic;

// ==============================================================================================
// http://NuGetter.codeplex.com/
//
// Author: Mark S. Nichols
//
// Copyright (c) 2013 Mark Nichols
//
// This source is subject to the Microsoft Permissive License. 
// ==============================================================================================
namespace TfsBuild.NuGetter.Activities
{
    /// <summary>
    /// Takes in a version pattern and turns it into a version number.
    /// </summary>
    [ToolboxBitmap(typeof(ConvertVersionPattern), "Resources.nugetter.ico")]
    [BuildActivity(HostEnvironmentOption.All)]
    [BuildExtension(HostEnvironmentOption.All)]
    public sealed class ConvertVersionPattern : CodeActivity
    {
        #region Workflow Arguments

        /// <summary>
        /// The pattern to convert
        /// </summary>
        [RequiredArgument]
        public InArgument<string> VersionPattern { get; set; }

        /// <summary>
        /// TFS build number in case the "B" pattern is used
        /// </summary>
        [RequiredArgument]
        public InArgument<string> BuildNumber { get; set; }

        /// <summary>
        /// The prefix value to add to the build number to make it unique compared to other builds
        /// </summary>
        [RequiredArgument]
        public InArgument<int> BuildNumberPrefix { get; set; }

        /// <summary>
        /// The converted version number 
        /// </summary>
        public OutArgument<string> ConvertedVersionNumber { get; set; }

        #endregion

        /// <summary>
        /// Processes the conversion of the version number
        /// </summary>
        /// <param name="context"></param>
        protected override void Execute(CodeActivityContext context)
        {
            // Get the values passed in
            var versionPattern = context.GetValue(VersionPattern);
            var buildNumber = context.GetValue(BuildNumber);
            var buildNumberPrefix = context.GetValue(BuildNumberPrefix);

            var version = DoConvertVersion(versionPattern, buildNumber, buildNumberPrefix);

            // Return the value back to the workflow
            context.SetValue(ConvertedVersionNumber, version);
        }

        public string DoConvertVersion(string versionPattern, string buildNumber, int buildNumberPrefix)
        {
            var version = new StringBuilder();

            // Validate the version pattern
            if (string.IsNullOrEmpty(versionPattern))
            {
                throw new ArgumentException("VersionPattern must contain the versioning pattern.");
            }
            
            var regex = new Regex(@"^(\d+)(\.\d{1,5})(\.\d{1,5})(\.\d{1,5})(([-|+][a-zA-Z0-9+-]*)|(\*))?$");  //^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$");

            var initialMatch = regex.IsMatch(versionPattern);

            if (initialMatch) return versionPattern;
            if (string.IsNullOrEmpty(buildNumber))
            {
                throw new ArgumentException("BuildNumber must contain the build value: use $(Rev:.r) at the end of the Build Number Format");
            }

            string buildNumberString = GetBuildFromBuildNumber(buildNumber, buildNumberPrefix);
            Dictionary<string, string> replacementValues = new Dictionary<string, string>()
            {
                { "YYYYMMDD", DateTime.Now.ToString("yyyyMMdd") },
                { "YYMMDD", DateTime.Now.ToString("yyMMdd") },
                { "YYYYMMDDB", DateTime.Now.ToString("yyyyMMdd") + buildNumberString },
                { "YYMMDDB", DateTime.Now.ToString("yyMMdd") + buildNumberString },
                { "YYYYMM", DateTime.Now.ToString("yyyyMM") },
                { "YYMM", DateTime.Now.ToString("yyMM") },
                { "YYYYMMB", DateTime.Now.ToString("yyyyMM") + buildNumberString },
                { "YYMMB", DateTime.Now.ToString("yyMM") + buildNumberString },
                { "JB", DateTime.Now.ToString("yy") + string.Format("{0:000}", DateTime.Now.DayOfYear) + buildNumberString },
                { "J", DateTime.Now.ToString("yy") + string.Format("{0:000}", DateTime.Now.DayOfYear) },
                { "YYYY", DateTime.Now.ToString("yyyy") },
                { "YY", DateTime.Now.ToString("yy") },
                { "MM", DateTime.Now.Month.ToString() },
                { "M", DateTime.Now.Month.ToString() },
                { "DD", DateTime.Now.Day.ToString() },
                { "D", DateTime.Now.Day.ToString() },
                { "B", GetBuildFromBuildNumber(buildNumber, buildNumberPrefix) },
            };

            string output = string.Empty;
            string versionString = versionPattern;
            char[] seperators = { '.', '-', ' ' };
            for (int i = 0; i < versionString.Length; i++)
            {
                int endIndex = versionString.IndexOfAny(seperators, i);
                if (endIndex != -1)
                {
                    string current = versionString.Substring(i, endIndex - i);

                    foreach (var replacementKvp in replacementValues)
                    {
                        if (current.Equals(replacementKvp.Key, StringComparison.InvariantCultureIgnoreCase))
                        {
                            current = ReplaceCaseInsensitive(current, replacementKvp.Key, replacementKvp.Value);
                        }
                    }

                    output += current;
                    output += versionString[endIndex];
                    i = endIndex;
                }
                else
                {
                    string current = versionString.Substring(i);

                    foreach (var replacementKvp in replacementValues)
                    {
                        if (current.Equals(replacementKvp.Key, StringComparison.InvariantCultureIgnoreCase))
                        {
                            current = ReplaceCaseInsensitive(current, replacementKvp.Key, replacementKvp.Value);
                        }
                    }

                    output += current;
                    break;
                }
            }

            return output;
        }

        private string ReplaceCaseInsensitive(string str, string oldValue, string newValue)
        {
            int prevPos = 0;
            string retval = str;
            // find the first occurence of oldValue
            int pos = retval.IndexOf(oldValue, StringComparison.InvariantCultureIgnoreCase);

            while (pos > -1)
            {
                // remove oldValue from the string
                retval = str.Remove(pos, oldValue.Length);

                // insert newValue in it's place
                retval = retval.Insert(pos, newValue);

                // check if oldValue is found further down
                prevPos = pos + newValue.Length;
                pos = retval.IndexOf(oldValue, prevPos, StringComparison.InvariantCultureIgnoreCase);
            }

            return retval;
        }

        private string GetBuildFromBuildNumber(string buildNumber, int buildNumberPrefix)
        {
            if (string.IsNullOrEmpty(buildNumber))
            {
                throw new ArgumentException("BuildNumber must contain the build value: use $(Rev:.r) at the end of the Build Number Format");
            }

            int buildNumberValue;

            // Attempt to parse - this should probably fails since it will only work if the only thing passed 
            //  in through the BuildNumber is a number.  This is typically something like: "Buildname.year.month.buildNumber"
            var isNumber = int.TryParse(buildNumber, out buildNumberValue);

            if (!isNumber)
            {
                var buildNumberArray = buildNumber.Split('.');

                const string exceptionString = "'Build Number Format' in the build definition must end with $(Rev:.r) to use the build number in the version pattern.  Suggested pattern: $(BuildDefinitionName)_$(Year:yyyy).$(Month).$(DayOfMonth)$(Rev:.r)";

                if (buildNumberArray.Length < 2)
                {
                    throw new ArgumentException(exceptionString);
                }

                isNumber = int.TryParse(buildNumberArray[buildNumberArray.Length - 1], out buildNumberValue);

                if (isNumber == false)
                {
                    throw new ArgumentException(exceptionString);
                }
            }

            buildNumberValue = AddBuildNumberPrefixIfNecessary(buildNumberPrefix, buildNumberValue);

            return buildNumberValue.ToString(CultureInfo.InvariantCulture);
        }

        private static int AddBuildNumberPrefixIfNecessary(int buildNumberPrefix, int buildNumberValue)
        {
            // If a BuildNumberPrefix is in place and the BuildNumber pattern is used then 
            // attempt to prefix the build number with the BuildNumberPrefix
            // The value of 10 is used since the prefix would have to be at least 10 to be at all useable
            if (buildNumberPrefix > 0)
            {
                if ((buildNumberValue >= buildNumberPrefix) || (buildNumberPrefix < 10))
                {
                    throw new ArgumentException("When the BuildNumberPrefix is used it must be at least 10 and also larger than the Build Number.");
                }

                // Prefix the build number to set it apart from any other build definition
                buildNumberValue += buildNumberPrefix;
            }

            return buildNumberValue;
        }
    }
}
