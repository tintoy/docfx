// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DocAsCode.Metadata.ManagedReference.MSBuildWorkspaces
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;

    using MSB = Microsoft.Build.Evaluation;
    using MSBC = Microsoft.Build.Construction;

    /// <summary>
    ///     Helper methods for working with MSBuild projects.
    /// </summary>
    public static class MSBuildHelper
    {
        /// <summary>
        ///     The names of well-known item metadata.
        /// </summary>
        public static readonly ImmutableSortedSet<string> WellknownMetadataNames =
            ImmutableSortedSet.Create(
                "FullPath",
                "RootDir",
                "Filename",
                "Extension",
                "RelativeDir",
                "Directory",
                "RecursiveDir",
                "Identity",
                "ModifiedTime",
                "CreatedTime",
                "AccessedTime"
            );

        /// <summary>
        ///     Create an MSBuild project collection.
        /// </summary>
        /// <param name="solutionDirectory">
        ///     The base (i.e. solution) directory.
        /// </param>
        /// <returns>
        ///     The project collection.
        /// </returns>
        public static MSB.ProjectCollection CreateProjectCollection(string solutionDirectory)
        {
            return CreateProjectCollection(solutionDirectory,
                DotNetRuntimeInfo.GetCurrent(solutionDirectory)
            );
        }

        /// <summary>
        ///     Create an MSBuild project collection.
        /// </summary>
        /// <param name="solutionDirectory">
        ///     The base (i.e. solution) directory.
        /// </param>
        /// <param name="runtimeInfo">
        ///     Information about the current .NET Core runtime.
        /// </param>
        /// <returns>
        ///     The project collection.
        /// </returns>
        public static MSB.ProjectCollection CreateProjectCollection(string solutionDirectory, DotNetRuntimeInfo runtimeInfo)
        {
            if (String.IsNullOrWhiteSpace(solutionDirectory))
                throw new ArgumentException("Argument cannot be null, empty, or entirely composed of whitespace: 'baseDir'.", nameof(solutionDirectory));

            if (runtimeInfo == null)
                throw new ArgumentNullException(nameof(runtimeInfo));

            if (String.IsNullOrWhiteSpace(runtimeInfo.BaseDirectory))
                throw new InvalidOperationException("Cannot determine base directory for .NET Core.");

            Dictionary<string, string> globalProperties = CreateGlobalMSBuildProperties(runtimeInfo, solutionDirectory);
            EnsureMSBuildEnvironment(globalProperties);

            var projectCollection = new MSB.ProjectCollection(globalProperties) { IsBuildEnabled = false };

            // Override toolset paths (for some reason these point to the main directory where the dotnet executable lives).
            var toolset = new MSB.Toolset(
                toolsVersion: "15.0",
                toolsPath: globalProperties["MSBuildExtensionsPath"],
                projectCollection: projectCollection,
                msbuildOverrideTasksPath: ""
            );
            projectCollection.AddToolset(toolset);

            return projectCollection;
        }

        /// <summary>
        ///     Create global properties for MSBuild.
        /// </summary>
        /// <param name="runtimeInfo">
        ///     Information about the current .NET Core runtime.
        /// </param>
        /// <param name="solutionDirectory">
        ///     The base (i.e. solution) directory.
        /// </param>
        /// <returns>
        ///     A dictionary containing the global properties.
        /// </returns>
        public static Dictionary<string, string> CreateGlobalMSBuildProperties(DotNetRuntimeInfo runtimeInfo, string solutionDirectory)
        {
            if (runtimeInfo == null)
                throw new ArgumentNullException(nameof(runtimeInfo));

            if (String.IsNullOrWhiteSpace(solutionDirectory))
                throw new ArgumentException("Argument cannot be null, empty, or entirely composed of whitespace: 'solutionDirectory'.", nameof(solutionDirectory));

            if (solutionDirectory.Length > 0 && solutionDirectory[solutionDirectory.Length - 1] != Path.DirectorySeparatorChar)
                solutionDirectory += Path.DirectorySeparatorChar;

            return new Dictionary<string, string>
            {
                [WellKnownPropertyNames.DesignTimeBuild] = "true",
                [WellKnownPropertyNames.BuildProjectReferences] = "false",
                [WellKnownPropertyNames.ResolveReferenceDependencies] = "true",
                [WellKnownPropertyNames.SolutionDir] = solutionDirectory,
                [WellKnownPropertyNames.MSBuildExtensionsPath] = runtimeInfo.BaseDirectory,
                [WellKnownPropertyNames.MSBuildSDKsPath] = Path.Combine(runtimeInfo.BaseDirectory, "Sdks"),
                [WellKnownPropertyNames.RoslynTargetsPath] = Path.Combine(runtimeInfo.BaseDirectory, "Roslyn")
            };
        }

        /// <summary>
        ///     Ensure that environment variables are populated using the specified MSBuild global properties.
        /// </summary>
        /// <param name="globalMSBuildProperties">
        ///     The MSBuild global properties
        /// </param>
        public static void EnsureMSBuildEnvironment(Dictionary<string, string> globalMSBuildProperties)
        {
            if (globalMSBuildProperties == null)
                throw new ArgumentNullException(nameof(globalMSBuildProperties));

            // Kinda sucks that the simplest way to get MSBuild to resolve SDKs correctly is using environment variables, but there you go.
            Environment.SetEnvironmentVariable(
                WellKnownPropertyNames.MSBuildExtensionsPath,
                globalMSBuildProperties[WellKnownPropertyNames.MSBuildExtensionsPath]
            );
            Environment.SetEnvironmentVariable(
                WellKnownPropertyNames.MSBuildSDKsPath,
                globalMSBuildProperties[WellKnownPropertyNames.MSBuildSDKsPath]
            );
        }

        /// <summary>
        ///     Does the specified property name represent a private property?
        /// </summary>
        /// <param name="propertyName">
        ///     The property name.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the property name starts with an underscore; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsPrivateProperty(string propertyName) => propertyName?.StartsWith("_") ?? false;

        /// <summary>
        ///     Does the specified metadata name represent a private property?
        /// </summary>
        /// <param name="metadataName">
        ///     The metadata name.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the metadata name starts with an underscore; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsPrivateMetadata(string metadataName) => metadataName?.StartsWith("_") ?? false;

        /// <summary>
        ///     Does the specified item type represent a private property?
        /// </summary>
        /// <param name="itemType">
        ///     The item type.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the item type starts with an underscore; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsPrivateItemType(string itemType) => itemType?.StartsWith("_") ?? false;

        /// <summary>
        ///     Determine whether the specified metadata name represents well-known (built-in) item metadata.
        /// </summary>
        /// <param name="metadataName">
        ///     The metadata name.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if <paramref name="metadataName"/> represents well-known item metadata; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsWellKnownItemMetadata(string metadataName) => WellknownMetadataNames.Contains(metadataName);

        /// <summary>
        ///     Create a copy of the project for caching.
        /// </summary>
        /// <param name="project">
        ///     The MSBuild project.
        /// </param>
        /// <returns>
        ///     The project copy (independent of original, but sharing the same <see cref="ProjectCollection"/>).
        /// </returns>
        /// <remarks>
        ///     You can only create a single cached copy for a given project.
        /// </remarks>
        public static MSB.Project CloneAsCachedProject(this MSB.Project project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            MSBC.ProjectRootElement clonedXml = project.Xml.DeepClone();
            var clonedProject = new MSB.Project(clonedXml, project.GlobalProperties, project.ToolsVersion, project.ProjectCollection)
            {
                FullPath = Path.ChangeExtension(project.FullPath,
                    ".cached" + Path.GetExtension(project.FullPath)
                )
            };

            return clonedProject;
        }

        /// <summary>
        ///     The names of well-known MSBuild properties.
        /// </summary>
        public static class WellKnownPropertyNames
        {
            /// <summary>
            ///     The "MSBuildExtensionsPath" property.
            /// </summary>
            public static readonly string MSBuildExtensionsPath = "MSBuildExtensionsPath";

            /// <summary>
            ///     The "MSBuildSDKsPath" property.
            /// </summary>
            public static readonly string MSBuildSDKsPath = "MSBuildSDKsPath";

            /// <summary>
            ///     The "SolutionDir" property.
            /// </summary>
            public static readonly string SolutionDir = "SolutionDir";

            /// <summary>
            ///     The "_ResolveReferenceDependencies" property.
            /// </summary>
            public static readonly string ResolveReferenceDependencies = "_ResolveReferenceDependencies";

            /// <summary>
            ///     The "DesignTimeBuild" property.
            /// </summary>
            public static readonly string DesignTimeBuild = "DesignTimeBuild";

            /// <summary>
            ///     The "BuildProjectReferences" property.
            /// </summary>
            public static readonly string BuildProjectReferences = "BuildProjectReferences";

            /// <summary>
            ///     The "RoslynTargetsPath" property.
            /// </summary>
            public static readonly string RoslynTargetsPath = "RoslynTargetsPath";
        }
    }
}
