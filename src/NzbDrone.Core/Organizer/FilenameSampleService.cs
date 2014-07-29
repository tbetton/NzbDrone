﻿using System;
using System.Collections.Generic;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Organizer
{
    public interface IFilenameSampleService
    {
        SampleResult GetStandardSample(NamingConfig nameSpec);
        SampleResult GetMultiEpisodeSample(NamingConfig nameSpec);
        SampleResult GetDailySample(NamingConfig nameSpec);
        SampleResult GetAnimeSample(NamingConfig nameSpec);
        String GetSeriesFolderSample(NamingConfig nameSpec);
        String GetSeasonFolderSample(NamingConfig nameSpec);
    }

    public class FilenameSampleService : IFilenameSampleService
    {
        private readonly IBuildFileNames _buildFileNames;
        private static Series _standardSeries;
        private static Series _dailySeries;
        private static Series _animeSeries;
        private static Episode _episode1;
        private static Episode _episode2;
        private static List<Episode> _singleEpisode;
        private static List<Episode> _multiEpisodes;
        private static EpisodeFile _singleEpisodeFile;
        private static EpisodeFile _multiEpisodeFile;
        private static EpisodeFile _dailyEpisodeFile;
        private static EpisodeFile _animeEpisodeFile;

        public FilenameSampleService(IBuildFileNames buildFileNames)
        {
            _buildFileNames = buildFileNames;

            _standardSeries = new Series
                              {
                                  SeriesType = SeriesTypes.Standard,
                                  Title = "Series Title"
                              };

            _dailySeries = new Series
            {
                SeriesType = SeriesTypes.Daily,
                Title = "Series Title"
            };

            _animeSeries = new Series
            {
                SeriesType = SeriesTypes.Anime,
                Title = "Series Title"
            };

            _episode1 = new Episode
            {
                SeasonNumber = 1,
                EpisodeNumber = 1,
                Title = "Episode Title (1)",
                AirDate = "2013-10-30",
                AbsoluteEpisodeNumber = 1
            };

            _episode2 = new Episode
            {
                SeasonNumber = 1,
                EpisodeNumber = 2,
                Title = "Episode Title (2)",
                AbsoluteEpisodeNumber = 2
            };

            _singleEpisode = new List<Episode> { _episode1 };
            _multiEpisodes = new List<Episode> { _episode1, _episode2 };

            _singleEpisodeFile = new EpisodeFile
            {
                Quality = new QualityModel(Quality.HDTV720p),
                RelativePath = "Series.Title.S01E01.720p.HDTV.x264-EVOLVE.mkv",
                ReleaseGroup = "RlsGrp"
            };

            _multiEpisodeFile = new EpisodeFile
            {
                Quality = new QualityModel(Quality.HDTV720p),
                RelativePath = "Series.Title.S01E01-E02.720p.HDTV.x264-EVOLVE.mkv",
                ReleaseGroup = "RlsGrp"
            };

            _dailyEpisodeFile = new EpisodeFile
            {
                Quality = new QualityModel(Quality.HDTV720p),
                RelativePath = "Series.Title.2013.10.30.HDTV.x264-EVOLVE.mkv",
                ReleaseGroup = "RlsGrp"
            };

            _animeEpisodeFile = new EpisodeFile
            {
                Quality = new QualityModel(Quality.HDTV720p),
                RelativePath = "Series.Title.001.HDTV.x264-EVOLVE.mkv",
                ReleaseGroup = "RlsGrp"
            };
        }

        public SampleResult GetStandardSample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                Filename = BuildSample(_singleEpisode, _standardSeries, _singleEpisodeFile, nameSpec),
                Series = _standardSeries,
                Episodes = _singleEpisode,
                EpisodeFile = _singleEpisodeFile
            };

            return result;
        }

        public SampleResult GetMultiEpisodeSample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                Filename = BuildSample(_multiEpisodes, _standardSeries, _multiEpisodeFile, nameSpec),
                Series = _standardSeries,
                Episodes = _multiEpisodes,
                EpisodeFile = _multiEpisodeFile
            };

            return result;
        }

        public SampleResult GetDailySample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                Filename = BuildSample(_singleEpisode, _dailySeries, _dailyEpisodeFile, nameSpec),
                Series = _dailySeries,
                Episodes = _singleEpisode,
                EpisodeFile = _dailyEpisodeFile
            };

            return result;
        }

        public SampleResult GetAnimeSample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                Filename = BuildSample(_singleEpisode, _animeSeries, _animeEpisodeFile, nameSpec),
                Series = _animeSeries,
                Episodes = _singleEpisode,
                EpisodeFile = _animeEpisodeFile
            };

            return result;
        }

        public string GetSeriesFolderSample(NamingConfig nameSpec)
        {
            return _buildFileNames.GetSeriesFolder(_standardSeries.Title, nameSpec);
        }

        public string GetSeasonFolderSample(NamingConfig nameSpec)
        {
            return _buildFileNames.GetSeasonFolder(_standardSeries.Title, _episode1.SeasonNumber, nameSpec);
        }

        private string BuildSample(List<Episode> episodes, Series series, EpisodeFile episodeFile, NamingConfig nameSpec)
        {
            try
            {
                return _buildFileNames.BuildFilename(episodes, series, episodeFile, nameSpec);
            }
            catch (NamingFormatException)
            {
                return String.Empty;
            }
        }
    }
}
