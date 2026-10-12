using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class ManagedQueueEnumerator_MoveNextAsync
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Yields_items_queued_before_the_enumerator_was_created_Async()
    {
        using ManagedQueue<int> queue = new();
        queue.Append(1);
        await using var enumerator = queue.GetAsyncEnumerator(10, TestContext.Current.CancellationToken);

        var moved = await enumerator.MoveNextAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        moved.Should().BeTrue();
        enumerator.Current.Should().Equal(1);
    }

    [Fact]
    public async Task Yields_the_rest_of_a_backlog_larger_than_the_count_Async()
    {
        using ManagedQueue<int> queue = new();
        await using var enumerator = queue.GetAsyncEnumerator(2, TestContext.Current.CancellationToken);
        queue.Append([1, 2, 3,]);
        await enumerator.MoveNextAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        var moved = await enumerator.MoveNextAsync().AsTask().WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        moved.Should().BeTrue();
        enumerator.Current.Should().Equal(3);
    }
}
