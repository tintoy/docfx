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
        ///     Generate a Roslyn <see cref="AdHocWorkspace"/> from a solution (.sln) file. 
        /// </summary>
        /// <param name="solutionPath">
        ///     The full path to the solution file.
        /// </param>
        /// <param name="msbuildProperties">
        ///     An optional <see cref="IDictionary{TKey, TValue}"/> containing global MSBuild properties used to configure the underlying project collection.
        /// </param>
        /// <returns>
        ///     The configured <see cref="AdhocWorkspace"/>.
        /// </returns>
        public static AdhocWorkspace FromSolutionFile(string solutionPath, IDictionary<string, string> msbuildProperties = null)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
                throw new ArgumentException($"Argument cannot be null, empty, or entirely composed of whitespace: {nameof(solutionPath)}.", nameof(solutionPath));

            return FromSolutionFile(
                new FileInfo(solutionPath)
            );
        }

        /// <summary>
        ///     Generate a Roslyn <see cref="AdHocWorkspace"/> from a solution (.sln) file. 
        /// </summary>
        /// <param name="solutionFile">
        ///     A <see cref="FileInfo"/> representing the solution file.
        /// </param>
        /// <param name="msbuildProperties">
        ///     An optional <see cref="IDictionary{TKey, TValue}"/> containing global MSBuild properties used to configure the underlying project collection.
        /// </param>
        /// <returns>
        ///     The configured <see cref="AdhocWorkspace"/>.
        /// </returns>
        public static AdhocWorkspace FromSolutionFile(FileInfo solutionFile, IDictionary<string, string> msbuildProperties = null)
        {
            if (solutionFile == null)
                throw new ArgumentNullException(nameof(solutionFile));

            AdhocWorkspace workspace = new AdhocWorkspace();

            Solution solution = workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionFile.FullName
            ));

            MSBC.SolutionFile msbuildSolution = MSBC.SolutionFile.Parse(solutionFile.FullName);

            MSB.ProjectCollection projectCollection = MSBuildHelper.CreateProjectCollection(
                solutionDirectory: solutionFile.Directory.FullName
            );
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
                var msbuildProject = projectCollection.LoadProject(solutionProject.AbsolutePath);

                string language;
                if (solutionProject.AbsolutePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    language = "C#";
                else if (solutionProject.AbsolutePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                    language = "VB";
                else
                    continue;

                var projectId = ProjectId.CreateNewId();

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

                var project = solution.GetProject(projectId);
                solution = solution.WithProjectCompilationOptions(projectId, project.CompilationOptions.WithSpecificDiagnosticOptions(
                    new Dictionary<string, ReportDiagnostic>
                    {
                        ["CS1701"] = ReportDiagnostic.Suppress
                    }
                ));

                workspace.TryApplyChanges(solution);
            }

            return workspace;
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
