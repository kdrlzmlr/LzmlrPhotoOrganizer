using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace PhotoVideoOrganizer.Tests
{
    [TestFixture]
    public class MainWindowTests
    {
        private Type _mainWindowType;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _mainWindowType = typeof(PhotoVideoOrganizer.MainWindow);
        }

        [Test]
        public async Task ComputeFileHashFastAsync_KnownContent_ReturnsExpectedHashAndSize()
        {
            // Arrange
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            string file = Path.Combine(tmp, "hello.txt");
            await File.WriteAllTextAsync(file, "hello", Encoding.UTF8);

            // Expected SHA256 computed via standard API
            byte[] expectedBytes;
            using (var sha = SHA256.Create())
            {
                expectedBytes = sha.ComputeHash(Encoding.UTF8.GetBytes("hello"));
            }
            string expectedHash = BitConverter.ToString(expectedBytes).Replace("-", "").ToLowerInvariant();
            long expectedSize = new FileInfo(file).Length;

            // Act
            var method = _mainWindowType.GetMethod("ComputeFileHashFastAsync", BindingFlags.NonPublic | BindingFlags.Static);

            ClassicAssert.IsNotNull(method, "Could not find ComputeFileHashFastAsync via reflection.");

            // Invoke static async method and await result
            var taskObj = (Task)method.Invoke(null, new object[] { file })!;
            await taskObj.ConfigureAwait(false);

            // The Task returns Task<(string hash, long size)>; need to extract Result property via reflection
            var resultProperty = taskObj.GetType().GetProperty("Result");
            ClassicAssert.IsNotNull(resultProperty, "Task did not expose Result property.");

            var tuple = resultProperty!.GetValue(taskObj);
            // tuple is a ValueTuple<string,long>
            var hashProp = tuple!.GetType().GetField("Item1");
            var sizeProp = tuple.GetType().GetField("Item2");
            string actualHash = (string)hashProp!.GetValue(tuple)!;
            long actualSize = (long)sizeProp!.GetValue(tuple)!;

            // Assert
            ClassicAssert.AreEqual(expectedHash, actualHash, "Hash mismatch");
            ClassicAssert.AreEqual(expectedSize, actualSize, "Size mismatch");

            // Cleanup
            Directory.Delete(tmp, recursive: true);
        }

        [Test]
        public void GetUniquePath_WhenFileExists_AppendsIncrement()
        {
            // Arrange
            var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmpDir);

            var basePath = Path.Combine(tmpDir, "file.txt");
            File.WriteAllText(basePath, "content");

            // Create file (1) to force increment to (2)
            var file1 = Path.Combine(tmpDir, "file (1).txt");
            File.WriteAllText(file1, "content 1");

            // Prepare instance without running constructor (avoid WPF initialization)
            var mainWindow = (object)FormatterServices.GetUninitializedObject(_mainWindowType);

            // Act
            var method = _mainWindowType.GetMethod("GetUniquePath", BindingFlags.NonPublic | BindingFlags.Instance);
            ClassicAssert.IsNotNull(method, "Could not find GetUniquePath via reflection.");

            var result = (string)method.Invoke(mainWindow, new object[] { basePath })!;

            // Assert: expected "file (2).txt"
            ClassicAssert.AreEqual(Path.Combine(tmpDir, "file (2).txt"), result);

            // Cleanup
            Directory.Delete(tmpDir, recursive: true);
        }

        [Test]
        public void GetDateTaken_WhenNoMetadata_FallsBackToCreationTime()
        {
            // Arrange
            var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmpDir);

            var file = Path.Combine(tmpDir, "no-meta.bin");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            DateTime expected = new DateTime(2020, 1, 2, 3, 4, 5);
            File.SetCreationTime(file, expected);

            // Prepare instance without running constructor (avoid WPF initialization)
            var mainWindow = (object)FormatterServices.GetUninitializedObject(_mainWindowType);

            var method = _mainWindowType.GetMethod("GetDateTaken", BindingFlags.NonPublic | BindingFlags.Instance);
            ClassicAssert.IsNotNull(method, "Could not find GetDateTaken via reflection.");

            // Act
            var result = (DateTime)method.Invoke(mainWindow, new object[] { file })!;

            // Assert (allow small difference due to filesystem rounding, but should be equal here)
            ClassicAssert.AreEqual(expected, result);

            // Cleanup
            Directory.Delete(tmpDir, recursive: true);
        }

        // Helper: demonstrates how you would call private instance methods if the instance needed public initialization.
        // UI-heavy methods (BrowseFolder, Start_Click, BrowseSource_Click, BrowseTarget_Click) are tightly coupled to WPF,
        // message boxes and dialogs and should be refactored to be testable. See below for recommended refactor.
    }
}