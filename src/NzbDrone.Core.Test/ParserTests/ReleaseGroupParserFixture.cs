using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class ReleaseGroupParserFixture : CoreTest
    {
        [TestCase("Castle.2009.S01E14.English.HDTV.XviD-LOL", "LOL")]
        [TestCase("Castle 2009 S01E14 English HDTV XviD LOL", "DRONE")]
        [TestCase("Acropolis Now S05 EXTRAS DVDRip XviD RUNNER", "DRONE")]
        [TestCase("Punky.Brewster.S01.EXTRAS.DVDRip.XviD-RUNNER", "RUNNER")]
        [TestCase("2020.NZ.2011.12.02.PDTV.XviD-C4TV", "C4TV")]
        [TestCase("The.Office.S03E115.DVDRip.XviD-OSiTV", "OSiTV")]
        [TestCase("The Office - S01E01 - Pilot [HTDV-480p]", "DRONE")]
        [TestCase("The Office - S01E01 - Pilot [HTDV-720p]", "DRONE")]
        [TestCase("The Office - S01E01 - Pilot [HTDV-1080p]", "DRONE")]
        [TestCase("The.Walking.Dead.S04E13.720p.WEB-DL.AAC2.0.H.264-Cyphanix", "Cyphanix")]
        [TestCase("Arrow.S02E01.720p.WEB-DL.DD5.1.H.264.mkv", "DRONE")]
        [TestCase("Series Title S01E01 Episode Title", "DRONE")]
        [TestCase("The Colbert Report - 2014-06-02 - Thomas Piketty.mkv", "DRONE")]
        [TestCase("Real Time with Bill Maher S12E17 May 23, 2014.mp4", "DRONE")]
        [TestCase("Reizen Waes - S01E08 - Transistri�, Zuid-Osseti� en Abchazi� SDTV.avi", "DRONE")]
        [TestCase("Simpsons 10x11 - Wild Barts Cant Be Broken [rl].avi", "DRONE")]
        public void should_parse_release_group(string title, string expected)
        {
            Parser.Parser.ParseReleaseGroup(title).Should().Be(expected);
        }

        [Test]
        public void should_not_include_extension_in_release_group()
        {
            const string path = @"C:\Test\Doctor.Who.2005.s01e01.internal.bdrip.x264-archivist.mkv";

            Parser.Parser.ParsePath(path).ReleaseGroup.Should().Be("archivist");
        }

        [TestCase("The.Longest.Mystery.S02E04.720p.WEB-DL.AAC2.0.H.264-EVL-RP", "EVL")]
        public void should_not_include_repost_in_release_group(string title, string expected)
        {
            Parser.Parser.ParseReleaseGroup(title).Should().Be(expected);
        }
    }
}
