using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.Devices.Client;
using NSubstitute;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class DeviceClientPool_Register
{
    [Fact]
    public async Task Returns_new_client()
    {
        List<IDeviceClient> createdClients = [];
        using DeviceClientPool pool = new((_, __) =>
        {
            var client = Substitute.For<IDeviceClient>();
            createdClients.Add(client);
            return client;
        });

        var client = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);
        var otherClient = await pool.Register("local", "my-other-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);

        client.Should().NotBeNull();
        otherClient.Should().NotBeNull();
        client.Should().NotBe(otherClient);
        client.InternalDeviceClient.Should().NotBe(otherClient.InternalDeviceClient);
        createdClients.Should().HaveCount(2);
    }

    [Fact]
    public async Task Returns_existing_client()
    {
        List<IDeviceClient> createdClients = [];
        using DeviceClientPool pool = new((_, __) =>
        {
            var client = Substitute.For<IDeviceClient>();
            createdClients.Add(client);
            return client;
        });

        var client = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);
        var otherClient = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);

        client.Should().NotBeNull();
        otherClient.Should().NotBeNull();
        client.Should().NotBe(otherClient);
        client.InternalDeviceClient.Should().Be(otherClient.InternalDeviceClient);
        createdClients.Should().ContainSingle();
    }
}

public class DeviceClientPool_Connect
{
    [Fact]
    public async Task Connects_client()
    {
        var internalClient = Substitute.For<IDeviceClient>();
        using DeviceClientPool pool = new((_, __) => internalClient);
        var client = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);

        await pool.Connect(client, TestContext.Current.CancellationToken);

        await internalClient.Received(1).OpenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_connect_client_twice()
    {
        var internalClient = Substitute.For<IDeviceClient>();
        using DeviceClientPool pool = new((_, __) => internalClient);
        var client = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);

        await pool.Connect(client, TestContext.Current.CancellationToken);
        await pool.Connect(client, TestContext.Current.CancellationToken);

        await internalClient.Received(1).OpenAsync(Arg.Any<CancellationToken>());
    }


    [Fact]
    public async Task Throws_when_client_not_registered()
    {
        using DeviceClientPool pool = new();
        await using VirtualDeviceClient client = new("my-device", Substitute.For<IDeviceClient>(), pool);

        var act = FluentActions.Invoking(() => pool.Connect(client, TestContext.Current.CancellationToken));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not*registered*");
    }
}

public class DeviceClientPool_Disconnect
{
    [Fact]
    public async Task Disconnects_client()
    {
        var internalClient = Substitute.For<IDeviceClient>();
        using DeviceClientPool pool = new((_, __) => internalClient);
        var client = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);
        await pool.Connect(client, TestContext.Current.CancellationToken);

        await pool.Disconnect(client, TestContext.Current.CancellationToken);

        await internalClient.Received(1).CloseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_disconnect_client_twice()
    {
        var internalClient = Substitute.For<IDeviceClient>();
        using DeviceClientPool pool = new((_, __) => internalClient);
        await using var client = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);
        await pool.Connect(client, TestContext.Current.CancellationToken);

        await pool.Disconnect(client, TestContext.Current.CancellationToken);
        await pool.Disconnect(client, TestContext.Current.CancellationToken);

        await internalClient.Received(1).CloseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_when_client_not_registered()
    {
        using DeviceClientPool pool = new();
        await using VirtualDeviceClient client = new("my-device", Substitute.For<IDeviceClient>(), pool);

        var act = FluentActions.Invoking(() => pool.Disconnect(client, TestContext.Current.CancellationToken));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not*registered*");
    }
}

public class DeviceClientPool_Unregister
{
    [Fact]
    public async Task Unregisters_client()
    {
        var internalClient = Substitute.For<IDeviceClient>();
        using DeviceClientPool pool = new((_, __) => internalClient);
        var client = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);

        await pool.Unregister(client);

        await internalClient.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Does_not_unregister_client_twice()
    {
        var internalClient = Substitute.For<IDeviceClient>();
        using DeviceClientPool pool = new((_, __) => internalClient);
        var client = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);

        await pool.Unregister(client);
        await pool.Unregister(client);

        await internalClient.Received(1).DisposeAsync();
    }
}

public class DeviceClientPool_InvokeDesiredPropertyUpdate
{
    [Fact]
    public async Task Invokes_desired_property_update()
    {
        var internalClient = Substitute.For<IDeviceClient>();
        using DeviceClientPool pool = new((_, __) => internalClient);
        DesiredPropertyUpdateCallback? callback = null;

        internalClient.SetDesiredPropertyUpdateCallbackAsync(Arg.Do<DesiredPropertyUpdateCallback>(c => callback = c),
            Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var clientInvoked = 0;
        var otherClientInvoked = 0;
        var client = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);
        var otherClient = await pool.Register("local", "my-device", Substitute.For<IAuthenticationMethod>(), TestContext.Current.CancellationToken);

        client.DesiredPropertyUpdate += (_, _) => clientInvoked++;
        otherClient.DesiredPropertyUpdate += (_, _) => otherClientInvoked++;

        callback.Should().NotBeNull();
        await callback(new(), new DeviceClientPool.Key("local", "my-device"));

        clientInvoked.Should().Be(1);
        otherClientInvoked.Should().Be(1);
    }
}
