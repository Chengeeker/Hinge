using Hinge.Core;
using System.Text;

namespace Hinge.Tests;

public class GameLayoutProtocolTests
{
    [Fact]
    public void LayoutSetRoundTripsAndRejectsInvalidCoordinates()
    {
        var document = new GameLayoutDocument
        {
            Selected = "profile",
            Presets =
            [
                new GameLayoutPreset
                {
                    Id = "profile",
                    Name = "基础布局",
                    Controls =
                    [
                        new GameLayoutControl
                        {
                            Id = "number-1", Label = "1", X = .8, Y = .25,
                            Size = .2, Aspect = .5, Binding = 49
                        }
                    ]
                }
            ]
        };

        byte[] payload = GameLayoutProtocol.SetPayload(document);
        Assert.True(GameLayoutProtocol.TryRead(payload, out string action, out var parsed));
        Assert.Equal("set", action);
        Assert.Equal(.5, Assert.Single(Assert.Single(parsed!.Presets).Controls).Aspect);

        document.Presets[0].Controls[0].X = double.NaN;
        Assert.Throws<FormatException>(() => GameLayoutProtocol.SetPayload(document));
    }

    [Fact]
    public void GetRequestAndAndroidDataResponseAreReadable()
    {
        Assert.True(GameLayoutProtocol.TryRead(
            GameLayoutProtocol.RequestPayload(), out string requestAction, out _));
        Assert.Equal("get", requestAction);

        byte[] response = Encoding.UTF8.GetBytes(
            "{\"version\":1,\"action\":\"data\",\"document\":{" +
            "\"version\":1,\"selected\":\"profile\",\"presets\":[{" +
            "\"id\":\"profile\",\"name\":\"基础布局\",\"controls\":[{" +
            "\"id\":\"number-1\",\"label\":\"1\",\"x\":0.8,\"y\":0.25," +
            "\"size\":0.2,\"aspect\":0.5,\"binding\":49,\"joystick\":false," +
            "\"directions\":[87,65,83,68],\"image\":null}]}]}}");

        Assert.True(GameLayoutProtocol.TryRead(response, out string action, out var document));
        Assert.Equal("data", action);
        Assert.Equal("profile", document!.Selected);
        Assert.Equal(.5, Assert.Single(Assert.Single(document.Presets).Controls).Aspect);
    }
}
