﻿using System.Linq;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.Queue
{
    public interface IQueueService
    {
        List<Queue> GetQueue();
    }

    public class QueueService : IQueueService
    {
        private readonly IDownloadTrackingService _downloadTrackingService;
        private readonly Logger _logger;

        public QueueService(IDownloadTrackingService downloadTrackingService, Logger logger)
        {
            _downloadTrackingService = downloadTrackingService;
            _logger = logger;
        }

        public List<Queue> GetQueue()
        {
            var queueItems = _downloadTrackingService.GetQueuedDownloads()
                .OrderBy(v => v.DownloadItem.RemainingTime)
                .ToList();

            return MapQueue(queueItems);
        }

        private List<Queue> MapQueue(IEnumerable<TrackedDownload> queueItems)
        {
            var queued = new List<Queue>();

            foreach (var queueItem in queueItems)
            {
                foreach (var episode in queueItem.DownloadItem.RemoteEpisode.Episodes)
                {
                    var queue = new Queue
                                {
                                    Id = episode.Id ^ (queueItem.DownloadItem.DownloadClientId.GetHashCode().GetHashCode() << 16),
                                    Series = queueItem.DownloadItem.RemoteEpisode.Series,
                                    Episode = episode,
                                    Quality = queueItem.DownloadItem.RemoteEpisode.ParsedEpisodeInfo.Quality,
                                    Title = queueItem.DownloadItem.Title,
                                    Size = queueItem.DownloadItem.TotalSize,
                                    Sizeleft = queueItem.DownloadItem.RemainingSize,
                                    Timeleft = queueItem.DownloadItem.RemainingTime,
                                    Status = queueItem.DownloadItem.Status.ToString(),
                                    RemoteEpisode = queueItem.DownloadItem.RemoteEpisode
                                };

                if (queueItem.HasError)
                    {
                        queue.ErrorMessage = queueItem.StatusMessage;
                    }

                    queued.Add(queue);
                }
            }

            return queued;
        }
    }
}
