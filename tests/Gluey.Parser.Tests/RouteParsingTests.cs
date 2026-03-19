// Copyright (C) 2026 Rebels Software
//
// Licensed under the Apache License, Version 2.0 (the "License")
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Gluey.Core.Models;
using GlueyParser = Gluey.Parser;

namespace Gluey.Parser.Tests;

public class RouteParsingTests
{
    [Fact]
    public void Parse_RouteBlock_ParsesConditions()
    {
        var source = @"
flow routing-test v1.0 {
  from http(""/webhook"")

  | json.parse(payload)
  | route {
      high_temp: temperature > 35
      normal: *
    }

  high_temp -> kafka(""alerts"")
  normal -> console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("routing-test", flow.Name);
        Assert.Equal("1.0", flow.Version);
        Assert.Equal(2, flow.PipelineSteps.Count);
        Assert.Equal("json.parse", flow.PipelineSteps[0].Type);
        Assert.Equal("route", flow.PipelineSteps[1].Type);
        Assert.Equal(2, flow.Routes.Count);
    }

    [Fact]
    public void Parse_RouteDestination_ParsesOutput()
    {
        var source = @"
flow routing-test v1.0 {
  from http(""/webhook"")

  | route {
      critical: level == ""high""
    }

  critical -> kafka(""critical-alerts"")
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Single(flow.Routes);
        var route = flow.Routes[0];
        Assert.Equal("critical", route.Name);
        Assert.Single(route.DestinationSteps);
        Assert.Equal("kafka", route.DestinationSteps[0].Type);
    }

    [Fact]
    public void Parse_CatchAllRoute_ParsesStarCondition()
    {
        var source = @"
flow routing-test v1.0 {
  from http(""/webhook"")

  | route {
      special: type == ""special""
      default: *
    }

  special -> kafka(""special"")
  default -> console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal(2, flow.Routes.Count);

        // Find the catch-all route
        var catchAll = flow.Routes.FirstOrDefault(r => r.Condition == "*");
        Assert.NotNull(catchAll);
        Assert.Equal("default", catchAll.Name);
        Assert.True(catchAll.IsCatchAll);
    }

    [Fact]
    public void Parse_OnErrorSkip_ParsesCorrectly()
    {
        var source = @"
flow error-test v1.0 {
  from http(""/webhook"")

  | json.parse(payload) {
      on_error: skip
    }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var parseStep = flow.PipelineSteps[0];
        Assert.NotNull(parseStep.OnError);
        Assert.Equal(OnErrorAction.Skip, parseStep.OnError.Action);
    }

    [Fact]
    public void Parse_OnErrorRouteTo_ParsesCorrectly()
    {
        var source = @"
flow error-test v1.0 {
  from http(""/webhook"")

  | json.parse(payload) {
      on_error: route_to(""parse_errors"")
    }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var parseStep = flow.PipelineSteps[0];
        Assert.NotNull(parseStep.OnError);
        Assert.Equal(OnErrorAction.RouteTo, parseStep.OnError.Action);
        Assert.Equal("parse_errors", parseStep.OnError.RouteName);
    }

    [Fact]
    public void Parse_ComplexRouteConditions_ParsesExpression()
    {
        var source = @"
flow complex-routing v1.0 {
  from http(""/webhook"")

  | route {
      critical: temperature > 80 || battery_level < 5
      warning: temperature > 65 && temperature <= 80
    }

  critical -> kafka(""alerts"")
  warning -> kafka(""warnings"")
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal(2, flow.Routes.Count);

        // The route step should have the conditions in its config
        var routeStep = flow.PipelineSteps.First(s => s.Type == "route");
        Assert.Equal(2, routeStep.Config.Count);
    }

    [Fact]
    public void Parse_MultipleOutputsRoute_ParsesArray()
    {
        var source = @"
flow multi-output v1.0 {
  from http(""/webhook"")

  | route {
      alert: level == ""critical""
    }

  alert -> [
    kafka(""alerts""),
    http(""https://webhook.example.com/alert"")
  ]
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var alertRoute = flow.Routes.First(r => r.Name == "alert");
        Assert.Equal(2, alertRoute.DestinationSteps.Count);
        Assert.Equal("kafka", alertRoute.DestinationSteps[0].Type);
        Assert.Equal("http", alertRoute.DestinationSteps[1].Type);
    }

    #region Route Mode: all Tests

    [Fact]
    public void Parse_RouteModeAll_StoresInConfig()
    {
        var source = @"
flow mode-all-test v1.0 {
  from http(""/webhook"")

  | route {
      mode: all
      ok: ok_count > 0
      nok: nok_count > 0
    }

  ok -> console()
  nok -> console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var routeStep = flow.PipelineSteps.First(s => s.Type == "route");

        // _route_mode should be stored in config
        Assert.True(routeStep.Config.ContainsKey("_route_mode"));
        Assert.Equal("all", routeStep.Config["_route_mode"].GetString());

        // Route conditions should still be parsed (mode is not a route condition)
        Assert.True(routeStep.Config.ContainsKey("ok"));
        Assert.True(routeStep.Config.ContainsKey("nok"));

        // 3 keys total: _route_mode, ok, nok
        Assert.Equal(3, routeStep.Config.Count);
    }

    [Fact]
    public void Parse_RouteModeFirst_StoresInConfig()
    {
        var source = @"
flow mode-first-test v1.0 {
  from http(""/webhook"")

  | route {
      mode: first
      ok: ok_count > 0
      nok: nok_count > 0
    }

  ok -> console()
  nok -> console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var routeStep = flow.PipelineSteps.First(s => s.Type == "route");
        Assert.True(routeStep.Config.ContainsKey("_route_mode"));
        Assert.Equal("first", routeStep.Config["_route_mode"].GetString());
    }

    [Fact]
    public void Parse_RouteModeInvalid_ThrowsParseException()
    {
        var source = @"
flow invalid-mode v1.0 {
  from http(""/webhook"")

  | route {
      mode: random
      ok: ok_count > 0
    }

  ok -> console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.Contains("Invalid route mode", ex.Message);
    }

    [Fact]
    public void Parse_RouteWithoutMode_HasNoModeInConfig()
    {
        var source = @"
flow no-mode-test v1.0 {
  from http(""/webhook"")

  | route {
      ok: ok_count > 0
      nok: *
    }

  ok -> console()
  nok -> console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var routeStep = flow.PipelineSteps.First(s => s.Type == "route");
        Assert.False(routeStep.Config.ContainsKey("_route_mode"));
        Assert.Equal(2, routeStep.Config.Count);
    }

    [Fact]
    public void Parse_RouteModeAll_DoesNotCreateModeRouteDestination()
    {
        var source = @"
flow mode-routes-test v1.0 {
  from http(""/webhook"")

  | route {
      mode: all
      ok: ok_count > 0
      nok: nok_count > 0
    }

  ok -> console()
  nok -> console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        // Should have exactly 2 route destinations (ok, nok) — NOT 3 (no "mode" route)
        Assert.Equal(2, flow.Routes.Count);
        Assert.Contains(flow.Routes, r => r.Name == "ok");
        Assert.Contains(flow.Routes, r => r.Name == "nok");
        Assert.DoesNotContain(flow.Routes, r => r.Name == "mode");
    }

    #endregion
}
