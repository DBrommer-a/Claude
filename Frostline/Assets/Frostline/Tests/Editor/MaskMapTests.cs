using System.Collections.Generic;
using Frostline.Art;
using NUnit.Framework;

namespace Frostline.Tests
{
    public class MaskMapTests
    {
        [Test]
        public void Pack_PutsChannelsWhereHdrpExpectsThem()
        {
            byte[] rgba = MaskMap.Pack(2, new byte[] { 10, 20 }, new byte[] { 200, 100 }, null, new byte[] { 55, 255 });
            Assert.That(rgba, Is.EqualTo(new byte[] { 10, 200, 255, 200, 20, 100, 255, 0 }));
        }

        [Test]
        public void Pack_UsesNeutralValuesForMissingMaps()
        {
            byte[] rgba = MaskMap.Pack(1, null, null, null, null);
            Assert.That(rgba, Is.EqualTo(new byte[] { 0, 255, 255, 128 }));
        }

        [Test]
        public void Pack_PrefersGlossinessOverRoughness()
        {
            byte[] rgba = MaskMap.Pack(1, null, null, null, new byte[] { 0 }, new byte[] { 77 });
            Assert.That(rgba[3], Is.EqualTo(77));
        }

        [Test]
        public void Pack_RejectsMismatchedSizes()
        {
            Assert.Throws<System.ArgumentException>(() => MaskMap.Pack(4, new byte[3], null, null, null));
        }

        [TestCase("rock_cliff_4K_Roughness.jpg", TextureRole.Roughness)]
        [TestCase("T_concrete_2K_BaseColor.png", TextureRole.Albedo)]
        [TestCase("snow_field_Normal.png", TextureRole.Normal)]
        [TestCase("ice_4K_AO.jpg", TextureRole.Occlusion)]
        [TestCase("metal_plate_Metalness.png", TextureRole.Metallic)]
        [TestCase("ground_Displacement.exr", TextureRole.Displacement)]
        [TestCase("preview.jpg", TextureRole.Unknown)]
        public void Naming_RecognizesMegascansSuffixes(string file, TextureRole role)
        {
            Assert.That(MegascansNaming.RoleOf(file), Is.EqualTo(role));
        }

        [Test]
        public void Naming_GroupsFilesOfOneMaterial()
        {
            var groups = MegascansNaming.Group(new[]
            {
                "Assets/Scans/rock_4K_BaseColor.jpg",
                "Assets/Scans/rock_4K_Roughness.jpg",
                "Assets/Scans/rock_4K_Normal.jpg",
                "Assets/Scans/snow_2K_BaseColor.jpg",
                "Assets/Scans/readme.txt",
            });
            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups["rock"].Count, Is.EqualTo(3));
            Assert.That(groups["snow"][TextureRole.Albedo], Does.EndWith("snow_2K_BaseColor.jpg"));
        }
    }
}
