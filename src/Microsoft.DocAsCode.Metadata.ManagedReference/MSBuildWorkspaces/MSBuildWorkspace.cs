// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DocAsCode.Metadata.ManagedReference.MSBuildWorkspaces
{
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Text;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using MSB = Microsoft.Build.Evaluation;
    using MSBC = Microsoft.Build.Construction;
    using MSBF = Microsoft.Build.Framework;
    using MSBX = Microsoft.Build.Execution;

    /// <summary>
    ///     Generates a Roslyn <see cref="AdHocWorkspace"/> from an MSBuild project. 
    /// </summary>
    public static class MSBuildWorkspace
    {
        /// <summary>
        ///     Open a solution (.sln) file, replacing the workspace's existing solution.
        /// </summary>
        /// <param name="workspace">
        ///     The target <see cref="AdhocWorkspace"/>.
        /// </param>
        /// <param name="solutionPath">
        ///     The full path to the solution file.
        /// </param>
        /// <param name="msbuildProperties">
        ///     An optional <see cref="IDictionary{TKey, TValue}"/> containing global MSBuild properties used to configure the underlying project collection.
        /// </param>
        /// <returns>
        ///     The loaded <see cref="Solution"/>.
        /// </returns>
        public static Solution OpenSolution(this AdhocWorkspace workspace, string solutionPath, IDictionary<string, string> msbuildProperties = null)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException($"Argument cannot be null, empty, or entirely composed of whitespace: {nameof(solutionPath)}.", nameof(solutionPath));

            return workspace.OpenSolution(
                new FileInfo(solutionPath)
            );
        }

        /// <summary>
        ///     Open a solution (.sln) file, replacing the workspace's existing solution.
        /// </summary>
        /// <param name="workspace">
        ///     The target <see cref="AdhocWorkspace"/>.
        /// </param>
        /// <param name="solutionFile">
        ///     A <see cref="FileInfo"/> representing the solution file.
        /// </param>
        /// <param name="msbuildProperties">
        ///     An optional <see cref="IDictionary{TKey, TValue}"/> containing global MSBuild properties used to configure the underlying project collection.
        /// </param>
        /// <returns>
        ///     The loaded <see cref="Solution"/>.
        /// </returns>
        public static Solution OpenSolution(this AdhocWorkspace workspace, FileInfo solutionFile, IDictionary<string, string> msbuildProperties = null)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            if (solutionFile == null)
                throw new ArgumentNullException(nameof(solutionFile));

            Solution solution = workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionFile.FullName
            ));

            MSBC.SolutionFile msbuildSolution = MSBC.SolutionFile.Parse(solutionFile.FullName);

            MSB.ProjectCollection projectCollection = MSBuildHelper.CreateProjectCollection(
                solutionDirectory: solutionFile.Directory.FullName
            );
            using (projectCollection)
            {
                if (msbuildProperties != null)
                {
                    foreach (string propertyName in msbuildProperties.Keys)
                    {
                        string propertyValue = msbuildProperties[propertyName];
                        projectCollection.SetGlobalProperty(propertyName, propertyValue);
                    }
                }

                foreach (var solutionProject in msbuildSolution.ProjectsInOrder)
                {
                    FileInfo projectFile = new FileInfo(solutionProject.AbsolutePath);
                    solution = solution.LoadMSBuildProject(projectFile, projectCollection);
                }

                workspace.TryApplyChanges(solution);
            }

            return workspace.CurrentSolution;
        }

        /// <summary>
        ///     Load a project into the workspace.
        /// </summary>
        /// <param name="workspace">
        ///     The target <see cref="AdhocWorkspace"/>.
        /// </param>
        /// <param name="projectFilePath">
        ///     The full path to the project file.
        /// </param>
        /// <param name="msbuildProperties">
        ///     An optional <see cref="IDictionary{TKey, TValue}"/> containing global MSBuild properties used to configure the underlying project collection.
        /// </param>
        /// <returns>
        ///     The loaded <see cref="Project"/>.
        /// </returns>
        public static Project OpenProject(this AdhocWorkspace workspace, string projectFilePath, IDictionary<string, string> msbuildProperties = null)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            if (string.IsNullOrWhiteSpace(projectFilePath))
                throw new ArgumentException($"Argument cannot be null, empty, or entirely composed of whitespace: {nameof(projectFilePath)}.", nameof(projectFilePath));

            return workspace.OpenProject(
                new FileInfo(projectFilePath),
                msbuildProperties
            );
        }

        /// <summary>
        ///     Load a project into the workspace.
        /// </summary>
        /// <param name="workspace">
        ///     The target <see cref="AdhocWorkspace"/>.
        /// </param>
        /// <param name="projectFile">
        ///     The full path to the <see cref="AdhocWorkspace"/>.
        /// </param>
        /// <param name="msbuildProperties">
        ///     An optional <see cref="IDictionary{TKey, TValue}"/> containing global MSBuild properties used to configure the underlying project collection.
        /// </param>
        /// <returns>
        ///     The loaded <see cref="Project"/>.
        /// </returns>
        public static Project OpenProject(this AdhocWorkspace workspace, FileInfo projectFile, IDictionary<string, string> msbuildProperties = null)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            if (projectFile == null)
                throw new ArgumentNullException(nameof(projectFile));

            // If the project is already loaded, reuse the existing instance.
            Project existingProject = workspace.CurrentSolution.Projects.FirstOrDefault(
                project => String.Equals(projectFile.FullName, project.FilePath, StringComparison.OrdinalIgnoreCase)
            );
            if (existingProject != null)
                return existingProject;

            MSB.ProjectCollection projectCollection = MSBuildHelper.CreateProjectCollection(
                solutionDirectory: projectFile.Directory.FullName
            );
            using (projectCollection)
            {
                if (msbuildProperties != null)
                {
                    foreach (string propertyName in msbuildProperties.Keys)
                    {
                        string propertyValue = msbuildProperties[propertyName];
                        projectCollection.SetGlobalProperty(propertyName, propertyValue);
                    }
                }

                Project project;
                Solution solution = workspace.CurrentSolution;
                solution = solution.LoadMSBuildProject(projectFile, projectCollection, out project);

                workspace.TryApplyChanges(solution);

                return project;
            }
        }

        /// <summary>
        ///     Load an MSBuild project and use its contents to populate the workspace's <see cref="Solution"/>.
        /// </summary>
        /// <param name="solution">
        ///     The <see cref="AdhocWorkspace"/>'s <see cref="Solution"/>.
        /// </param>
        /// <param name="projectFile">
        ///     A <see cref="FileInfo"/> representing the MSBuild project file.
        /// </param>
        /// <param name="projectCollection">
        ///     The <see cref="MSB.ProjectCollection"/> that the project will be loaded into.
        /// </param>
        /// <returns>
        ///     The updated <see cref="Solution"/>.
        /// </returns>
        static Solution LoadMSBuildProject(this Solution solution, FileInfo projectFile, MSB.ProjectCollection projectCollection) => solution.LoadMSBuildProject(projectFile, projectCollection, out _);

        /// <summary>
        ///     Load an MSBuild project and use its contents to populate the workspace's <see cref="Solution"/>.
        /// </summary>
        /// <param name="solution">
        ///     The <see cref="AdhocWorkspace"/>'s <see cref="Solution"/>.
        /// </param>
        /// <param name="projectFile">
        ///     A <see cref="FileInfo"/> representing the MSBuild project file.
        /// </param>
        /// <param name="projectCollection">
        ///     The <see cref="MSB.ProjectCollection"/> that the project will be loaded into.
        /// </param>
        /// <param name="project">
        ///     Receives the loaded project (or <c>null</c> if the project is not of a supported type).
        /// </param>
        /// <returns>
        ///     The updated <see cref="Solution"/>.
        /// </returns>
        static Solution LoadMSBuildProject(this Solution solution, FileInfo projectFile, MSB.ProjectCollection projectCollection, out Project project)
        {
            if (solution == null)
                throw new ArgumentNullException(nameof(solution));

            if (projectFile == null)
                throw new ArgumentNullException(nameof(projectFile));

            if (projectCollection == null)
                throw new ArgumentNullException(nameof(projectCollection));

            var msbuildProject = projectCollection.LoadProject(projectFile.FullName);

            string language;
            if (String.Equals(projectFile.Extension, ".csproj", StringComparison.OrdinalIgnoreCase))
                language = "C#";
            if (String.Equals(projectFile.Extension, ".csproj", StringComparison.OrdinalIgnoreCase))
                language = "VB";
            else
            {
                // Unsupported project type.
                project = null;

                return solution;
            }

            var projectId = ProjectId.CreateNewId();

            // Add source files in a single batch.
            List<DocumentInfo> projectDocuments = new List<DocumentInfo>();
            foreach (MSB.ProjectItem item in msbuildProject.GetItems("Compile"))
            {
                string itemPath = item.GetMetadataValue("FullPath");

                projectDocuments.Add(
                    DocumentInfo.Create(
                        DocumentId.CreateNewId(projectId),
                        name: Path.GetFileName(itemPath),
                        filePath: itemPath,
                        loader: TextLoader.From(
                            TextAndVersion.Create(
                                SourceText.From(File.ReadAllText(itemPath)),
                                VersionStamp.Create()
                            )
                        )
                    )
                );
            }

            List<MetadataReference> references = new List<MetadataReference>();
            foreach (string assemblyPath in ResolveReferences(msbuildProject))
            {
                references.Add(
                    MetadataReference.CreateFromFile(assemblyPath, MetadataReferenceProperties.Assembly)
                );
            }

            solution = solution.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                name: Path.GetFileNameWithoutExtension(msbuildProject.FullPath),
                assemblyName: Path.GetFileNameWithoutExtension(msbuildProject.FullPath),
                language: language,
                filePath: msbuildProject.FullPath,
                outputFilePath: msbuildProject.GetPropertyValue("TargetPath"),
                documents: projectDocuments,
                metadataReferences: references
            ));

            project = solution.GetProject(projectId);
            solution = solution.WithProjectCompilationOptions(projectId, project.CompilationOptions.WithSpecificDiagnosticOptions(
                new Dictionary<string, ReportDiagnostic>
                {
                    ["CS1701"] = ReportDiagnostic.Suppress
                }
            ));

            project = solution.GetProject(projectId); // Yes, really.

            return solution;
        }

        /// <summary>
        ///     Resolve referenced assembly paths from the specified project.
        /// </summary>
        /// <param name="msbuildProject">
        ///     The MSBuild project to examine.
        /// </param>
        /// <returns>
        ///     A sequence of referenced assembly paths.
        /// </returns>
        static IEnumerable<string> ResolveReferences(MSB.Project msbuildProject)
        {
            MSBX.ProjectInstance snapshot = msbuildProject.CreateProjectInstance();

            IDictionary<string, MSBX.TargetResult> outputs;
            if (!snapshot.Build(new string[] { "ResolveAssemblyReferences" }, null, out outputs))
                yield break;

            foreach (string targetName in outputs.Keys)
            {
                MSBX.TargetResult targetResult = outputs[targetName];
                MSBF.ITaskItem[] items = targetResult.Items.ToArray();

                foreach (MSBF.ITaskItem item in items)
                    yield return item.GetMetadata("FullPath");
            }
        }
    }
}
