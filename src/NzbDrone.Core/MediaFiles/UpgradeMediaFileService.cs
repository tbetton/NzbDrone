﻿using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public interface IUpgradeMediaFiles
    {
        EpisodeFileMoveResult UpgradeEpisodeFile(EpisodeFile episodeFile, LocalEpisode localEpisode, bool copyOnly = false);
    }

    public class UpgradeMediaFileService : IUpgradeMediaFiles
    {
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMoveEpisodeFiles _episodeFileMover;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public UpgradeMediaFileService(IRecycleBinProvider recycleBinProvider,
                                       IMediaFileService mediaFileService,
                                       IMoveEpisodeFiles episodeFileMover,
                                       IDiskProvider diskProvider,
                                       Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _episodeFileMover = episodeFileMover;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public EpisodeFileMoveResult UpgradeEpisodeFile(EpisodeFile episodeFile, LocalEpisode localEpisode, bool copyOnly = false)
        {
            var moveFileResult = new EpisodeFileMoveResult();
            var existingFiles = localEpisode.Episodes
                                            .Where(e => e.EpisodeFileId > 0)
                                            .Select(e => e.EpisodeFile.Value)
                                            .GroupBy(e => e.Id);

            foreach (var existingFile in existingFiles)
            {
                var file = existingFile.First();
                var episodeFilePath = Path.Combine(localEpisode.Series.Path, episodeFile.RelativePath);

                if (_diskProvider.FileExists(episodeFilePath))
                {
                    _logger.Debug("Removing existing episode file: {0}", file);
                    _recycleBinProvider.DeleteFile(episodeFilePath);
                }

                moveFileResult.OldFiles.Add(file);
                _mediaFileService.Delete(file, true);
            }

            if (copyOnly)
            {
                moveFileResult.EpisodeFile = _episodeFileMover.CopyEpisodeFile(episodeFile, localEpisode);
            }
            else
            {
                moveFileResult.EpisodeFile = _episodeFileMover.MoveEpisodeFile(episodeFile, localEpisode);
            }

            return moveFileResult;
        }
    }
}
