using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NSubstitute;

namespace Shared.Tests;

[TestClass]
public class ArgUtilsTests
{
    [TestMethod]
    public void TryDequeueString_ShouldReturnTrue_WhenValueIsPresent()
    {
        var args = new Queue<string>(new[] { "value" });
        var showUsage = Substitute.For<Action<string>>();
        string? argValue;

        var result = ArgUtils.TryDequeueString(args, showUsage, "argName", out argValue);

        Assert.IsTrue(result);
        Assert.AreEqual("value", argValue);
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [TestMethod]
    public void TryDequeueString_ShouldReturnFalse_WhenValueIsNull()
    {
        var args = new Queue<string>(new[] { "" });
        var showUsage = Substitute.For<Action<string>>();
        string? argValue;

        var result = ArgUtils.TryDequeueString(args, showUsage, "argName", out argValue);

        Assert.IsFalse(result);
        Assert.IsNull(argValue);
        showUsage.Received(1).Invoke("Argument 'argName' has an empty value.");
    }

    [TestMethod]
    public void TryDequeueRepo_ShouldReturnTrue_WhenValueIsValid()
    {
        var args = new Queue<string>(new[] { "org/repo" });
        var showUsage = Substitute.For<Action<string>>();
        string? org;
        string? repo;

        var result = ArgUtils.TryDequeueRepo(args, showUsage, "argName", out org, out repo);

        Assert.IsTrue(result);
        Assert.AreEqual("org", org);
        Assert.AreEqual("repo", repo);
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [TestMethod]
    public void TryDequeueRepo_ShouldReturnFalse_WhenValueIsInvalid()
    {
        var args = new Queue<string>(new[] { "invalid" });
        var showUsage = Substitute.For<Action<string>>();
        string? org;
        string? repo;

        var result = ArgUtils.TryDequeueRepo(args, showUsage, "argName", out org, out repo);

        Assert.IsFalse(result);
        Assert.IsNull(org);
        Assert.IsNull(repo);
        showUsage.Received(1).Invoke("Argument 'argName' has an empty value or is not in the format of '{org}/{repo}'.");
    }

    [TestMethod]
    public void TryDequeueRepoList_ShouldReturnTrue_WhenValuesAreValid()
    {
        var args = new Queue<string>(new[] { "org/repo1,org/repo2" });
        var showUsage = Substitute.For<Action<string>>();
        string? org;
        List<string>? repos;

        var result = ArgUtils.TryDequeueRepoList(args, showUsage, "argName", out org, out repos);

        Assert.IsTrue(result);
        Assert.AreEqual("org", org);
        CollectionAssert.AreEqual(new List<string> { "repo1", "repo2" }, repos);
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [TestMethod]
    public void TryDequeueRepoList_ShouldReturnFalse_WhenValuesAreInvalid()
    {
        var args = new Queue<string>(new[] { "invalid" });
        var showUsage = Substitute.For<Action<string>>();
        string? org;
        List<string>? repos;

        var result = ArgUtils.TryDequeueRepoList(args, showUsage, "argName", out org, out repos);

        Assert.IsFalse(result);
        Assert.IsNull(org);
        Assert.IsNull(repos);
        showUsage.Received(1).Invoke("Argument '--repo' is not in the format of '{org}/{repo}': invalid");
    }

    [TestMethod]
    public void TryDequeueLabelPrefix_ShouldReturnTrue_WhenValueIsValid()
    {
        var args = new Queue<string>(new[] { "area-" });
        var showUsage = Substitute.For<Action<string>>();
        Func<string, bool>? labelPredicate;

        var result = ArgUtils.TryDequeueLabelPrefix(args, showUsage, "argName", out labelPredicate);

        Assert.IsTrue(result);
        Assert.IsNotNull(labelPredicate);
        Assert.IsTrue(labelPredicate("area-label"));
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [TestMethod]
    public void TryDequeueLabelPrefix_ShouldReturnFalse_WhenValueIsInvalid()
    {
        var args = new Queue<string>(new[] { "area" });
        var showUsage = Substitute.For<Action<string>>();
        Func<string, bool>? labelPredicate;

        var result = ArgUtils.TryDequeueLabelPrefix(args, showUsage, "argName", out labelPredicate);

        Assert.IsFalse(result);
        Assert.IsNull(labelPredicate);
        showUsage.Received(1).Invoke(Arg.Is<string>(s => s.Contains("Argument 'argName' must end in something other than a letter or number.")));
    }

    [TestMethod]
    public void TryDequeuePath_ShouldReturnTrue_WhenValueIsValid()
    {
        var args = new Queue<string>(new[] { "C:\\path\\to\\file" });
        var showUsage = Substitute.For<Action<string>>();
        string? path;

        var result = ArgUtils.TryDequeuePath(args, showUsage, "argName", out path);

        Assert.IsTrue(result);
        Assert.AreEqual("C:\\path\\to\\file", path);
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [TestMethod]
    public void TryDequeuePath_ShouldReturnFalse_WhenValueIsInvalid()
    {
        var args = new Queue<string>(new[] { "" });
        var showUsage = Substitute.For<Action<string>>();
        string? path;

        var result = ArgUtils.TryDequeuePath(args, showUsage, "argName", out path);

        Assert.IsFalse(result);
        Assert.IsNull(path);
        showUsage.Received(1).Invoke("Argument 'argName' has an empty value.");
    }

    [TestMethod]
    public void TryDequeueStringArray_ValidInput_ReturnsTrue()
    {
        var args = new Queue<string>(new[] { "value1,value2,value3" });
        var showUsage = Substitute.For<Action<string>>();
        bool result = ArgUtils.TryDequeueStringArray(args, showUsage, "argName", out string[]? argValues);

        Assert.IsTrue(result);
        Assert.IsNotNull(argValues);
        Assert.AreEqual(3, argValues.Length);
        CollectionAssert.Contains(argValues, "value1");
        CollectionAssert.Contains(argValues, "value2");
        CollectionAssert.Contains(argValues, "value3");
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [TestMethod]
    public void TryDequeueInt_ValidInput_ReturnsTrue()
    {
        var args = new Queue<string>(new[] { "123" });
        var showUsage = Substitute.For<Action<string>>();
        bool result = ArgUtils.TryDequeueInt(args, showUsage, "argName", out int? argValue);

        Assert.IsTrue(result);
        Assert.IsNotNull(argValue);
        Assert.AreEqual(123, argValue);
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [TestMethod]
    public void TryDequeueIntArray_ValidInput_ReturnsTrue()
    {
        var args = new Queue<string>(new[] { "1,2,3" });
        var showUsage = Substitute.For<Action<string>>();
        bool result = ArgUtils.TryDequeueIntArray(args, showUsage, "argName", out int[]? argValues);

        Assert.IsTrue(result);
        Assert.IsNotNull(argValues);
        Assert.AreEqual(3, argValues.Length);
        CollectionAssert.Contains(argValues, 1);
        CollectionAssert.Contains(argValues, 2);
        CollectionAssert.Contains(argValues, 3);
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [TestMethod]
    public void TryDequeueFloat_ValidInput_ReturnsTrue()
    {
        var args = new Queue<string>(new[] { "123.45" });
        var showUsage = Substitute.For<Action<string>>();
        bool result = ArgUtils.TryDequeueFloat(args, showUsage, "argName", out float? argValue);

        Assert.IsTrue(result);
        Assert.IsNotNull(argValue);
        Assert.AreEqual(123.45f, argValue);
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [TestMethod]
    public void TryDequeueNumberRanges_ValidInput_ReturnsTrue()
    {
        var args = new Queue<string>(new[] { "1-3,5,7-9" });
        var showUsage = Substitute.For<Action<string>>();
        bool result = ArgUtils.TryDequeueNumberRanges(args, showUsage, "argName", out List<ulong>? argValues);

        Assert.IsTrue(result);
        Assert.IsNotNull(argValues);
        Assert.AreEqual(7, argValues.Count);
        CollectionAssert.Contains(argValues, (ulong)1);
        CollectionAssert.Contains(argValues, (ulong)2);
        CollectionAssert.Contains(argValues, (ulong)3);
        CollectionAssert.Contains(argValues, (ulong)5);
        CollectionAssert.Contains(argValues, (ulong)7);
        CollectionAssert.Contains(argValues, (ulong)8);
        CollectionAssert.Contains(argValues, (ulong)9);
        showUsage.DidNotReceive().Invoke(Arg.Any<string>());
    }
}
