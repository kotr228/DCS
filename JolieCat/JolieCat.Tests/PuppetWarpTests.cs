using JolieCat.Core.Transform;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class PuppetWarpTests
    {
        [Fact]
        public void NoPinsOrUnmovedPins_LeaveEverythingInPlace()
        {
            var puppet = new PuppetWarp(200, 100);
            Assert.Equal(new SKPoint(30, 40), puppet.Map(new SKPoint(30, 40)));

            puppet.Pins.Add(new PuppetWarp.Pin(new SKPoint(20, 20)));
            puppet.Pins.Add(new PuppetWarp.Pin(new SKPoint(180, 80)));
            var p = puppet.Map(new SKPoint(100, 50));
            Assert.InRange(p.X, 99.9f, 100.1f);
            Assert.InRange(p.Y, 49.9f, 50.1f);
        }

        [Fact]
        public void OnePin_ShiftsEverything()
        {
            var puppet = new PuppetWarp(200, 100);
            puppet.Pins.Add(new PuppetWarp.Pin(new SKPoint(50, 50)) { Current = new SKPoint(60, 45) });
            var p = puppet.Map(new SKPoint(150, 20));
            Assert.InRange(p.X, 159.9f, 160.1f);
            Assert.InRange(p.Y, 14.9f, 15.1f);
        }

        [Fact]
        public void DraggingOnePin_BendsAroundTheFixedOne()
        {
            var puppet = new PuppetWarp(300, 100);
            puppet.Pins.Add(new PuppetWarp.Pin(new SKPoint(20, 50)));                                    // held
            puppet.Pins.Add(new PuppetWarp.Pin(new SKPoint(280, 50)) { Current = new SKPoint(280, 90) }); // pulled down

            Assert.Equal(new SKPoint(280, 90), puppet.Map(new SKPoint(280, 50)));   // pins are exact
            Assert.Equal(new SKPoint(20, 50), puppet.Map(new SKPoint(20, 50)));
            var nearFixed = puppet.Map(new SKPoint(30, 50));
            var middle = puppet.Map(new SKPoint(150, 50));
            Assert.True(nearFixed.Y - 50 < 4, $"near the held pin {nearFixed}");
            Assert.InRange(middle.Y, 55, 85);   // in between, part of the way

            // Rigid: the length between two nearby points is kept roughly.
            var a = puppet.Map(new SKPoint(150, 50));
            var b = puppet.Map(new SKPoint(160, 50));
            var length = SKPoint.Distance(a, b);
            Assert.InRange(length, 8.5f, 11.5f);
        }

        [Fact]
        public void Unmap_FindsThePointShowingThere()
        {
            var puppet = new PuppetWarp(300, 100);
            puppet.Pins.Add(new PuppetWarp.Pin(new SKPoint(20, 50)));
            puppet.Pins.Add(new PuppetWarp.Pin(new SKPoint(280, 50)) { Current = new SKPoint(260, 80) });
            var shown = new SKPoint(140, 64);
            var source = puppet.Unmap(shown);
            var back = puppet.Map(source);
            Assert.InRange(SKPoint.Distance(back, shown), 0, 0.5f);
        }
    }
}
