using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public interface IEpisodeService
    {
        Episode GetEpisode(int id);
        Episode FindEpisode(int seriesId, int seasonNumber, int episodeNumber);
        Episode FindEpisode(int seriesId, int absoluteEpisodeNumber);
        Episode FindEpisodeByName(int seriesId, int seasonNumber, string episodeTitle);
        List<Episode> FindEpisodesBySceneNumbering(int seriesId, int seasonNumber, int episodeNumber);
        Episode FindEpisodeBySceneNumbering(int seriesId, int sceneAbsoluteEpisodeNumber);
        Episode GetEpisode(int seriesId, String date);
        Episode FindEpisode(int seriesId, String date);
        List<Episode> GetEpisodeBySeries(int seriesId);
        List<Episode> GetEpisodesBySeason(int seriesId, int seasonNumber);
        List<Episode> EpisodesWithFiles(int seriesId);
        PagingSpec<Episode> EpisodesWithoutFiles(PagingSpec<Episode> pagingSpec);
        List<Episode> GetEpisodesByFileId(int episodeFileId);
        void UpdateEpisode(Episode episode);
        void SetEpisodeMonitored(int episodeId, bool monitored);
        void UpdateEpisodes(List<Episode> episodes);
        List<Episode> EpisodesBetweenDates(DateTime start, DateTime end);
        void InsertMany(List<Episode> episodes);
        void UpdateMany(List<Episode> episodes);
        void DeleteMany(List<Episode> episodes);
        void SetEpisodeMonitoredBySeason(int seriesId, int seasonNumber, bool monitored);
    }

    public class EpisodeService : IEpisodeService,
        IHandle<EpisodeFileDeletedEvent>,
        IHandle<EpisodeFileAddedEvent>,
        IHandleAsync<SeriesDeletedEvent>
    {
        private readonly IEpisodeRepository _episodeRepository;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public EpisodeService(IEpisodeRepository episodeRepository, IConfigService configService, Logger logger)
        {
            _episodeRepository = episodeRepository;
            _configService = configService;
            _logger = logger;
        }

        public Episode GetEpisode(int id)
        {
            return _episodeRepository.Get(id);
        }

        public Episode FindEpisode(int seriesId, int seasonNumber, int episodeNumber)
        {
            return _episodeRepository.Find(seriesId, seasonNumber, episodeNumber);
        }

        public Episode FindEpisode(int seriesId, int absoluteEpisodeNumber)
        {
            return _episodeRepository.Find(seriesId, absoluteEpisodeNumber);
        }

        public List<Episode> FindEpisodesBySceneNumbering(int seriesId, int seasonNumber, int episodeNumber)
        {
            return _episodeRepository.FindEpisodesBySceneNumbering(seriesId, seasonNumber, episodeNumber);
        }

        public Episode FindEpisodeBySceneNumbering(int seriesId, int sceneAbsoluteEpisodeNumber)
        {
            return _episodeRepository.FindEpisodeBySceneNumbering(seriesId, sceneAbsoluteEpisodeNumber);
        }

        public Episode GetEpisode(int seriesId, String date)
        {
            return _episodeRepository.Get(seriesId, date);
        }

        public Episode FindEpisode(int seriesId, String date)
        {
            return _episodeRepository.Find(seriesId, date);
        }

        public List<Episode> GetEpisodeBySeries(int seriesId)
        {
            return _episodeRepository.GetEpisodes(seriesId).ToList();
        }

        public List<Episode> GetEpisodesBySeason(int seriesId, int seasonNumber)
        {
            return _episodeRepository.GetEpisodes(seriesId, seasonNumber);
        }
        
        public Episode FindEpisodeByName(int seriesId, int seasonNumber, string episodeTitle) 
        {
            // TODO: can replace this search mechanism with something smarter/faster/better
            var search = Parser.Parser.NormalizeEpisodeTitle(episodeTitle);
            return _episodeRepository.GetEpisodes(seriesId, seasonNumber)
                .FirstOrDefault(e => 
                {
                    // normalize episode title
                    string title = Parser.Parser.NormalizeEpisodeTitle(e.Title);
                    // find episode title within search string
                    return (title.Length > 0) && search.Contains(title); 
                });
        }

        public List<Episode> EpisodesWithFiles(int seriesId)
        {
            return _episodeRepository.EpisodesWithFiles(seriesId);
        }

        public PagingSpec<Episode> EpisodesWithoutFiles(PagingSpec<Episode> pagingSpec)
        {
            var episodeResult = _episodeRepository.EpisodesWithoutFiles(pagingSpec, false);

            return episodeResult;
        }

        public List<Episode> GetEpisodesByFileId(int episodeFileId)
        {
            return _episodeRepository.GetEpisodeByFileId(episodeFileId);
        }

        public void UpdateEpisode(Episode episode)
        {
            _episodeRepository.Update(episode);
        }

        public void SetEpisodeMonitored(int episodeId, bool monitored)
        {
            var episode = _episodeRepository.Get(episodeId);
            _episodeRepository.SetMonitoredFlat(episode, monitored);

            _logger.Debug("Monitored flag for Episode:{0} was set to {1}", episodeId, monitored);
        }

        public void SetEpisodeMonitoredBySeason(int seriesId, int seasonNumber, bool monitored)
        {
            _episodeRepository.SetMonitoredBySeason(seriesId, seasonNumber, monitored);
        }

        public void UpdateEpisodes(List<Episode> episodes)
        {
            _episodeRepository.UpdateMany(episodes);
        }

        public List<Episode> EpisodesBetweenDates(DateTime start, DateTime end)
        {
            var episodes = _episodeRepository.EpisodesBetweenDates(start.ToUniversalTime(), end.ToUniversalTime());

            return episodes;
        }

        public void InsertMany(List<Episode> episodes)
        {
            _episodeRepository.InsertMany(episodes);
        }

        public void UpdateMany(List<Episode> episodes)
        {
            _episodeRepository.UpdateMany(episodes);
        }

        public void DeleteMany(List<Episode> episodes)
        {
            _episodeRepository.DeleteMany(episodes);
        }

        public void HandleAsync(SeriesDeletedEvent message)
        {
            var episodes = GetEpisodeBySeries(message.Series.Id);
            _episodeRepository.DeleteMany(episodes);
        }

        public void Handle(EpisodeFileDeletedEvent message)
        {
            foreach (var episode in GetEpisodesByFileId(message.EpisodeFile.Id))
            {
                _logger.Debug("Detaching episode {0} from file.", episode.Id);
                episode.EpisodeFileId = 0;

                if (!message.ForUpgrade && _configService.AutoUnmonitorPreviouslyDownloadedEpisodes)
                {
                    episode.Monitored = false;
                }

                UpdateEpisode(episode);
            }
        }

        public void Handle(EpisodeFileAddedEvent message)
        {
            foreach (var episode in message.EpisodeFile.Episodes.Value)
            {
                _episodeRepository.SetFileId(episode.Id, message.EpisodeFile.Id);
                _logger.Debug("Linking [{0}] > [{1}]", message.EpisodeFile.RelativePath, episode);
            }
        }
    }
}