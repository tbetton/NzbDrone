﻿using System;
using System.IO;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Tv.Commands;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public class MoveSeriesService : IExecute<MoveSeriesCommand>
    {
        private readonly ISeriesService _seriesService;
        private readonly IBuildFileNames _filenameBuilder;
        private readonly IDiskProvider _diskProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MoveSeriesService(ISeriesService seriesService,
                                 IBuildFileNames filenameBuilder,
                                 IDiskProvider diskProvider,
                                 IEventAggregator eventAggregator,
                                 Logger logger)
        {
            _seriesService = seriesService;
            _filenameBuilder = filenameBuilder;
            _diskProvider = diskProvider;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Execute(MoveSeriesCommand message)
        {
            var series = _seriesService.GetSeries(message.SeriesId);
            var source = message.SourcePath;
            var destination = message.DestinationPath;

            if (!message.DestinationRootFolder.IsNullOrWhiteSpace())
            {
                _logger.Debug("Buiding destination path using root folder: {0} and the series title", message.DestinationRootFolder);
                destination = Path.Combine(message.DestinationRootFolder, _filenameBuilder.GetSeriesFolder(series.Title));
            }

            _logger.ProgressInfo("Moving {0} from '{1}' to '{2}'", series.Title, source, destination);

            //TODO: Move to transactional disk operations
            try
            {
                _diskProvider.MoveFolder(source, destination);
            }
            catch (IOException ex)
            {
                var errorMessage = String.Format("Unable to move series from '{0}' to '{1}'", source, destination);

                _logger.ErrorException(errorMessage, ex);
                throw;
            }

            _logger.ProgressInfo("{0} moved successfully to {1}", series.Title, series.Path);

            //Update the series path to the new path
            series.Path = destination;
            series = _seriesService.UpdateSeries(series);

            _eventAggregator.PublishEvent(new SeriesMovedEvent(series, source, destination));
        }
    }
}
