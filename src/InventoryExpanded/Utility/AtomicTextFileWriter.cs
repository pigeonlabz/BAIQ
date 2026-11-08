using System.Text;

namespace Backpack;

internal static class AtomicTextFileWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    internal static bool TryWrite(string destinationPath, string contents, out string reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(destinationPath) || contents == null)
        {
            reason = "destination or payload is unavailable";
            return false;
        }

        string temporaryPath = null;
        string backupPath = null;
        var replacedExisting = false;
        var createdDestination = false;
        try
        {
            var destination = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                reason = "destination directory is unavailable";
                return false;
            }

            temporaryPath = Path.Combine(directory, ".Backpack.json." + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, Utf8WithoutBom, leaveOpen: true))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (!string.Equals(File.ReadAllText(temporaryPath), contents, StringComparison.Ordinal))
            {
                reason = "staged payload readback mismatch";
                return false;
            }

            if (File.Exists(destination))
            {
                backupPath = Path.Combine(directory, ".Backpack.json." + Guid.NewGuid().ToString("N") + ".bak");
                File.Replace(temporaryPath, destination, backupPath);
                replacedExisting = true;
            }
            else
            {
                File.Move(temporaryPath, destination);
                createdDestination = true;
            }

            temporaryPath = null;
            if (!string.Equals(File.ReadAllText(destination), contents, StringComparison.Ordinal))
            {
                reason = "committed payload readback mismatch";
                if (replacedExisting && backupPath != null && File.Exists(backupPath))
                {
                    File.Replace(backupPath, destination, destinationBackupFileName: null);
                    backupPath = null;
                    reason += "; previous sidecar restored";
                }
                else if (createdDestination && File.Exists(destination))
                {
                    File.Delete(destination);
                }

                return false;
            }

            if (backupPath != null)
            {
                try
                {
                    File.Delete(backupPath);
                }
                catch
                {
                    // The committed sidecar has already passed readback; an old backup is safe to leave.
                }

                backupPath = null;
            }

            return true;
        }
        catch (Exception exception)
        {
            reason = $"atomic sidecar write failed safely ({exception.GetType().Name})";
            if (backupPath != null && File.Exists(backupPath))
                reason += "; previous sidecar backup remains available";
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // A uniquely named leftover temp file is harmless; never remove the destination.
                }
            }
        }
    }
}
