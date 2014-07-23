using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Api.REST;
using NzbDrone.Common;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Api.Mapping;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;

namespace NzbDrone.Api.EpisodeFiles
{
    public class EpisodeModule : NzbDroneRestModuleWithSignalR<EpisodeFileResource, EpisodeFile>,
                                 IHandle<EpisodeFileAddedEvent>
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;

        public EpisodeModule(ICommandExecutor commandExecutor,
                             IMediaFileService mediaFileService,
                             IRecycleBinProvider recycleBinProvider,
                             ISeriesService seriesService,
                             Logger logger)
            : base(commandExecutor)
        {
            _mediaFileService = mediaFileService;
            _recycleBinProvider = recycleBinProvider;
            _seriesService = seriesService;
            _logger = logger;
            GetResourceById = GetEpisodeFile;
            GetResourceAll = GetEpisodeFiles;
            UpdateResource = SetQuality;
            DeleteResource = DeleteEpisodeFile;
        }

        private EpisodeFileResource GetEpisodeFile(int id)
        {
            return _mediaFileService.Get(id).InjectTo<EpisodeFileResource>();
        }

        private List<EpisodeFileResource> GetEpisodeFiles()
        {
            var seriesId = (int?)Request.Query.SeriesId;

            if (seriesId == null)
            {
                throw new BadRequestException("seriesId is missing");
            }

            return ToListResource(() => _mediaFileService.GetFilesBySeries(seriesId.Value));
        }

        private void SetQuality(EpisodeFileResource episodeFileResource)
        {
            var episodeFile = _mediaFileService.Get(episodeFileResource.Id);
            episodeFile.Quality = episodeFileResource.Quality;
            _mediaFileService.Update(episodeFile);
        }

        private void DeleteEpisodeFile(int id)
        {
            var episodeFile = _mediaFileService.Get(id);
            var series = _seriesService.GetSeries(episodeFile.SeriesId);
            var fullPath = Path.Combine(series.Path, episodeFile.RelativePath);

            _logger.Info("Deleting episode file: {0}", fullPath);
            _recycleBinProvider.DeleteFile(fullPath);
            _mediaFileService.Delete(episodeFile);
        }

        public void Handle(EpisodeFileAddedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, message.EpisodeFile.Id);
        }
    }
}