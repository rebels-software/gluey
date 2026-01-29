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
}
