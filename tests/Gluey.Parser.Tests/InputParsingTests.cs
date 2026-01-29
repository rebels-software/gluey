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

using System.Text.Json;
using GlueyParser = Gluey.Parser;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for input block parsing with config
/// </summary>
public class InputParsingTests
{
    [Fact]
    public void Parse_HttpInput_ExtractsType()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("http", flow.Input.Type);
    }

    [Fact]
    public void Parse_HttpInput_ExtractsUrl()
    {
        var source = @"
flow test v1.0 {
  from http(""/api/sensors"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("/api/sensors", flow.Input.Url);
    }

    [Fact]
    public void Parse_HttpInputWithConfig_ExtractsConfigValues()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") {
    port: 8080
    timeout: 30
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal(8080, flow.Input.Config["port"].GetInt64());
        Assert.Equal(30, flow.Input.Config["timeout"].GetInt64());
    }

    [Fact]
    public void Parse_MqttInput_ParsesBrokerUrl()
    {
        var source = @"
flow test v1.0 {
  from mqtt(""mqtt://broker.local:1883"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("mqtt", flow.Input.Type);
        Assert.Equal("mqtt://broker.local:1883", flow.Input.Url);
    }

    [Fact]
    public void Parse_MqttInputWithTopics_ParsesArrayConfig()
    {
        var source = @"
flow test v1.0 {
  from mqtt(""mqtt://broker.local:1883"") {
    topics: [""sensors/+/temperature"", ""devices/+/status""]
    qos: 1
    client_id: ""gluey-worker""
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.True(flow.Input.Config.ContainsKey("topics"));
        var topics = flow.Input.Config["topics"];
        Assert.Equal(JsonValueKind.Array, topics.ValueKind);
        Assert.Equal(2, topics.GetArrayLength());

        Assert.Equal(1, flow.Input.Config["qos"].GetInt64());
        Assert.Equal("gluey-worker", flow.Input.Config["client_id"].GetString());
    }

    [Fact]
    public void Parse_InputWithBooleanConfig_ParsesCorrectly()
    {
        var source = @"
flow test v1.0 {
  from mqtt(""mqtts://secure.broker:8883"") {
    tls: true
    verify_cert: false
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.True(flow.Input.Config["tls"].GetBoolean());
        Assert.False(flow.Input.Config["verify_cert"].GetBoolean());
    }

    [Fact]
    public void Parse_InputWithNestedConfig_ParsesObjectValue()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") {
    headers: {
      Content_Type: ""application/json""
      X_Api_Key: ""secret""
    }
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var headers = flow.Input.Config["headers"];
        Assert.Equal(JsonValueKind.Object, headers.ValueKind);
    }

    [Fact]
    public void Parse_KafkaInput_ParsesMultipleBrokers()
    {
        var source = @"
flow test v1.0 {
  from kafka(""sensor-data"") {
    brokers: [""kafka1:9092"", ""kafka2:9092"", ""kafka3:9092""]
    group_id: ""gluey-consumers""
    offset: ""earliest""
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("kafka", flow.Input.Type);
        Assert.Equal("sensor-data", flow.Input.Url);

        var brokers = flow.Input.Config["brokers"];
        Assert.Equal(3, brokers.GetArrayLength());
        Assert.Equal("gluey-consumers", flow.Input.Config["group_id"].GetString());
    }

    [Fact]
    public void Parse_InputWithoutConfig_HasEmptyConfig()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Empty(flow.Input.Config);
    }

    [Fact]
    public void Parse_InputWithFloatConfig_ParsesDecimalValues()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") {
    timeout_seconds: 30.5
    retry_factor: 1.5
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal(30.5, flow.Input.Config["timeout_seconds"].GetDouble());
        Assert.Equal(1.5, flow.Input.Config["retry_factor"].GetDouble());
    }

    [Fact]
    public void Parse_InputWithHexConfig_ParsesHexNumber()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") {
    magic_number: 0xABCD
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal(0xABCD, flow.Input.Config["magic_number"].GetInt64());
    }
}
