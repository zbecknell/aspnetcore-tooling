// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;

namespace Microsoft.CodeAnalysis.Razor
{
    [Shared]
    [Export(typeof(ProjectWorkspaceStateGenerator))]
    [Export(typeof(ProjectSnapshotChangeTrigger))]
    internal class ProjectWorkspaceStateGenerator : ProjectSnapshotChangeTrigger
    {
        private static readonly IReadOnlyList<TagHelperDescriptor> EmptyTagHelpers = Array.Empty<TagHelperDescriptor>();
        private readonly ForegroundDispatcher _foregroundDispatcher;
        private ProjectSnapshotManagerBase _projectManager;

        private readonly Dictionary<string, UpdateItem> _updates;
        private TagHelperResolver _tagHelperResolver;

        [ImportingConstructor]
        public ProjectWorkspaceStateGenerator(ForegroundDispatcher foregroundDispatcher)
        {
            if (foregroundDispatcher == null)
            {
                throw new ArgumentNullException(nameof(foregroundDispatcher));
            }

            _foregroundDispatcher = foregroundDispatcher;

            _updates = new Dictionary<string, UpdateItem>(FilePathComparer.Instance);
        }

        public override void Initialize(ProjectSnapshotManagerBase projectManager)
        {
            if (projectManager == null)
            {
                throw new ArgumentNullException(nameof(projectManager));
            }

            _projectManager = projectManager;

            var razorLanguageServices = _projectManager.Workspace.Services.GetLanguageServices(RazorLanguage.Name);
            _tagHelperResolver = razorLanguageServices.GetRequiredService<TagHelperResolver>();
        }

        public void UpdateWorkspaceState(Project workspaceProject, ProjectSnapshot projectSnapshot)
        {
            if (projectSnapshot == null)
            {
                throw new ArgumentNullException(nameof(projectSnapshot));
            }

            _foregroundDispatcher.AssertForegroundThread();

            if (_updates.TryGetValue(projectSnapshot.FilePath, out var updateItem) &&
                !updateItem.Task.IsCompleted)
            {
                updateItem.Cts.Cancel();
            }

            updateItem?.Cts.Dispose();

            var cts = new CancellationTokenSource();
            var updateTask = Task.Factory.StartNew(
                () => UpdateWorkspaceStateAsync(workspaceProject, projectSnapshot, cts.Token),
                cts.Token,
                TaskCreationOptions.None,
                _foregroundDispatcher.BackgroundScheduler).Unwrap();
            updateTask.ConfigureAwait(false);
            updateItem = new UpdateItem(updateTask, cts);
            _updates[projectSnapshot.FilePath] = updateItem;
        }

        private async Task UpdateWorkspaceStateAsync(Project workspaceProject, ProjectSnapshot projectSnapshot, CancellationToken cancellationToken)
        {
            try
            {
                _foregroundDispatcher.AssertBackgroundThread();

                var workspaceState = ProjectWorkspaceState.Default;
                try
                {
                    if (workspaceProject != null)
                    {
                        var tagHelperResolutionResult = await _tagHelperResolver.GetTagHelpersAsync(workspaceProject, projectSnapshot, cancellationToken);
                        workspaceState = new ProjectWorkspaceState(tagHelperResolutionResult.Descriptors);
                    }
                }
                catch (Exception ex)
                {
                    await Task.Factory.StartNew(
                       () => _projectManager.ReportError(ex, projectSnapshot),
                       CancellationToken.None, // Don't allow errors to be cancelled
                       TaskCreationOptions.None,
                       _foregroundDispatcher.ForegroundScheduler).ConfigureAwait(false);
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await Task.Factory.StartNew(
                    () =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        ReportWorkspaceStateChange(projectSnapshot.FilePath, workspaceState);
                    },
                    cancellationToken,
                    TaskCreationOptions.None,
                    _foregroundDispatcher.ForegroundScheduler).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // This is something totally unexpected, let's just send it over to the project manager.
                await Task.Factory.StartNew(
                    () => _projectManager.ReportError(ex),
                    CancellationToken.None, // Don't allow errors to be cancelled
                    TaskCreationOptions.None,
                    _foregroundDispatcher.ForegroundScheduler).ConfigureAwait(false);
            }
        }

        private void ReportWorkspaceStateChange(string projectFilePath, ProjectWorkspaceState workspaceStateChange)
        {
            _foregroundDispatcher.AssertForegroundThread();

            _projectManager.ProjectWorkspaceStateChanged(projectFilePath, workspaceStateChange);
        }

        private class UpdateItem
        {
            public UpdateItem(Task task, CancellationTokenSource cts)
            {
                if (task == null)
                {
                    throw new ArgumentNullException(nameof(task));
                }

                if (cts == null)
                {
                    throw new ArgumentNullException(nameof(cts));
                }

                Task = task;
                Cts = cts;
            }

            public Task Task { get; }

            public CancellationTokenSource Cts { get; }
        }
    }
}