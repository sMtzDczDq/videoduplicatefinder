// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */

using Microsoft.Data.Sqlite;
using VDF.Core;
using VDF.Core.Utils;

namespace VDF.DbExport {
	/// <summary>
	/// Standalone tool that converts a VDF ScannedFiles.db (a MemoryPack/protobuf binary
	/// blob) into a normalized SQLite database so it can be browsed with any SQLite tool
	/// (Rider Database viewer, sqlite3 CLI, DBeaver, …).
	///
	/// Usage:
	///   vdf-dbexport &lt;input.db&gt; [output.sqlite]
	///
	/// If output is omitted it defaults to "ScannedFiles.sqlite" next to the input file.
	/// </summary>
	static class Program {
	static int Main(string[] args) {
		if (args.Length == 0 || args[0] is "-h" or "--help" or "/?") {
			Console.WriteLine("Usage: vdf-dbexport <input.db> [output.sqlite]");
			Console.WriteLine("  Converts a VDF ScannedFiles.db into SQLite for browsing with any SQLite viewer.");
			return args.Length == 0 ? 1 : 0;
		}

		string input = Path.GetFullPath(args[0]);
		if (!File.Exists(input)) {
			Console.Error.WriteLine($"Input file not found: {input}");
			return 1;
		}

		string output = args.Length > 1 ? Path.GetFullPath(args[1])
			: Path.ChangeExtension(input, ".sqlite");

		try {
			DatabaseWrapper? wrapper = DatabaseUtils.ReadDatabaseFromFile(input);
			if (wrapper == null) {
				Console.Error.WriteLine($"Failed to read database (missing, empty, or unreadable): {input}");
				return 1;
			}

			Export(wrapper, output);
			Console.WriteLine($"Wrote {wrapper.Entries.Count:N0} entries to {output}");
			return 0;
		}
		catch (Exception ex) {
			Console.Error.WriteLine($"Export failed: {ex}");
			return 1;
		}
	}

	static void Export(DatabaseWrapper wrapper, string output) {
		// Delete any stale output so we never mix old and new rows.
		if (File.Exists(output))
			File.Delete(output);

		string connString = new SqliteConnectionStringBuilder { DataSource = output, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
		using var conn = new SqliteConnection(connString);
		conn.Open();
		using var tx = conn.BeginTransaction();

		CreateSchema(conn, wrapper);

		using var entryCmd = conn.CreateCommand();
		entryCmd.Transaction = tx;
		entryCmd.CommandText = @"
			INSERT INTO file_entries
				(path, folder, flags, date_created, date_modified, file_size, audio_fingerprint, os_hash, is_image)
			VALUES ($path, $folder, $flags, $created, $modified, $size, $fingerprint, $os_hash, $is_image);";
		var pPath = entryCmd.Parameters.Add("$path", SqliteType.Text);
		var pFolder = entryCmd.Parameters.Add("$folder", SqliteType.Text);
		var pFlags = entryCmd.Parameters.Add("$flags", SqliteType.Integer);
		var pCreated = entryCmd.Parameters.Add("$created", SqliteType.Text);
		var pModified = entryCmd.Parameters.Add("$modified", SqliteType.Text);
		var pSize = entryCmd.Parameters.Add("$size", SqliteType.Integer);
		var pFingerprint = entryCmd.Parameters.Add("$fingerprint", SqliteType.Blob);
		var pOsHash = entryCmd.Parameters.Add("$os_hash", SqliteType.Text);
		var pIsImage = entryCmd.Parameters.Add("$is_image", SqliteType.Integer);

		using var grayCmd = conn.CreateCommand();
		grayCmd.Transaction = tx;
		grayCmd.CommandText = "INSERT INTO gray_bytes (entry_path, position, data) VALUES ($path, $pos, $data);";
		var gEntryPath = grayCmd.Parameters.Add("$path", SqliteType.Text);
		var gPos = grayCmd.Parameters.Add("$pos", SqliteType.Real);
		var gData = grayCmd.Parameters.Add("$data", SqliteType.Blob);

		using var phashCmd = conn.CreateCommand();
		phashCmd.Transaction = tx;
		phashCmd.CommandText = "INSERT INTO p_hashes (entry_path, position, hash) VALUES ($path, $pos, $hash);";
		var hEntryPath = phashCmd.Parameters.Add("$path", SqliteType.Text);
		var hPos = phashCmd.Parameters.Add("$pos", SqliteType.Real);
		var hHash = phashCmd.Parameters.Add("$hash", SqliteType.Integer);

		using var mediaCmd = conn.CreateCommand();
		mediaCmd.Transaction = tx;
		mediaCmd.CommandText = "INSERT INTO media_info (entry_path, duration_secs) VALUES ($path, $dur);";
		var mEntryPath = mediaCmd.Parameters.Add("$path", SqliteType.Text);
		var mDur = mediaCmd.Parameters.Add("$dur", SqliteType.Real);

		using var streamCmd = conn.CreateCommand();
		streamCmd.Transaction = tx;
		streamCmd.CommandText = @"
			INSERT INTO stream_info
				(media_path, idx, codec_name, codec_long_name, codec_type, pixel_format,
				 width, height, sample_rate, channel_layout, bit_rate, frame_rate, channels, hdr_format)
			VALUES ($path, $idx, $codec, $codec_long, $type, $pix, $w, $h, $sample, $layout, $bitrate, $fps, $channels, $hdr);";
		var sPath = streamCmd.Parameters.Add("$path", SqliteType.Text);
		var sIdx = streamCmd.Parameters.Add("$idx", SqliteType.Text);
		var sCodec = streamCmd.Parameters.Add("$codec", SqliteType.Text);
		var sCodecLong = streamCmd.Parameters.Add("$codec_long", SqliteType.Text);
		var sType = streamCmd.Parameters.Add("$type", SqliteType.Text);
		var sPix = streamCmd.Parameters.Add("$pix", SqliteType.Text);
		var sW = streamCmd.Parameters.Add("$w", SqliteType.Integer);
		var sH = streamCmd.Parameters.Add("$h", SqliteType.Integer);
		var sSample = streamCmd.Parameters.Add("$sample", SqliteType.Integer);
		var sLayout = streamCmd.Parameters.Add("$layout", SqliteType.Text);
		var sBitrate = streamCmd.Parameters.Add("$bitrate", SqliteType.Integer);
		var sFps = streamCmd.Parameters.Add("$fps", SqliteType.Real);
		var sChannels = streamCmd.Parameters.Add("$channels", SqliteType.Integer);
		var sHdr = streamCmd.Parameters.Add("$hdr", SqliteType.Text);

		foreach (FileEntry entry in wrapper.Entries) {
			pPath.Value = entry.Path;
			pFolder.Value = (object?)entry.Folder ?? DBNull.Value;
			pFlags.Value = (int)entry.Flags;
			pCreated.Value = entry.DateCreated != default ? entry.DateCreated.ToString("o") : DBNull.Value;
			pModified.Value = entry.DateModified != default ? entry.DateModified.ToString("o") : DBNull.Value;
			pSize.Value = entry.FileSize;
			pFingerprint.Value = ToBlob(entry.AudioFingerprint);
			pOsHash.Value = (object?)entry.OsHash ?? DBNull.Value;
			pIsImage.Value = entry.IsImage ? 1 : 0;
			entryCmd.ExecuteNonQuery();

			if (entry.grayBytes.Count > 0) {
				foreach (var kv in entry.grayBytes) {
					gEntryPath.Value = entry.Path;
					gPos.Value = kv.Key;
					gData.Value = (object?)kv.Value ?? DBNull.Value;
					grayCmd.ExecuteNonQuery();
				}
			}

			if (entry.PHashes.Count > 0) {
				foreach (var kv in entry.PHashes) {
					hEntryPath.Value = entry.Path;
					hPos.Value = kv.Key;
					hHash.Value = kv.Value.HasValue ? unchecked((long)kv.Value.Value) : DBNull.Value;
					phashCmd.ExecuteNonQuery();
				}
			}

			if (entry.mediaInfo is { } media) {
				mEntryPath.Value = entry.Path;
				mDur.Value = media.Duration.TotalSeconds;
				mediaCmd.ExecuteNonQuery();

				foreach (var s in media.Streams) {
					sPath.Value = entry.Path;
					sIdx.Value = (object?)s.Index ?? DBNull.Value;
					sCodec.Value = (object?)s.CodecName ?? DBNull.Value;
					sCodecLong.Value = (object?)s.CodecLongName ?? DBNull.Value;
					sType.Value = (object?)s.CodecType ?? DBNull.Value;
					sPix.Value = (object?)s.PixelFormat ?? DBNull.Value;
					sW.Value = s.Width;
					sH.Value = s.Height;
					sSample.Value = s.SampleRate;
					sLayout.Value = (object?)s.ChannelLayout ?? DBNull.Value;
					sBitrate.Value = s.BitRate;
					sFps.Value = s.FrameRate;
					sChannels.Value = s.Channels;
					sHdr.Value = (object?)s.HdrFormat ?? DBNull.Value;
					streamCmd.ExecuteNonQuery();
				}
			}
		}

		tx.Commit();
	}

	static void CreateSchema(SqliteConnection conn, DatabaseWrapper wrapper) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
			PRAGMA journal_mode=WAL;

			CREATE TABLE meta (
				key   TEXT PRIMARY KEY,
				value TEXT NOT NULL
			);

			INSERT INTO meta (key, value) VALUES ('version', $version);
			INSERT INTO meta (key, value) VALUES ('image_hash_pipeline', $pipeline);

			CREATE TABLE file_entries (
				path              TEXT PRIMARY KEY COLLATE NOCASE,
				folder            TEXT,
				flags             INTEGER NOT NULL,
				date_created      TEXT,
				date_modified     TEXT,
				file_size         INTEGER NOT NULL,
				audio_fingerprint BLOB,
				os_hash           TEXT,
				is_image          INTEGER NOT NULL
			);
			CREATE INDEX idx_file_entries_folder ON file_entries(folder);
			CREATE INDEX idx_file_entries_is_image ON file_entries(is_image);

			CREATE TABLE gray_bytes (
				entry_path TEXT NOT NULL COLLATE NOCASE REFERENCES file_entries(path),
				position   REAL    NOT NULL,
				data       BLOB
			);
			CREATE INDEX idx_gray_bytes_entry ON gray_bytes(entry_path);

			CREATE TABLE p_hashes (
				entry_path TEXT NOT NULL COLLATE NOCASE REFERENCES file_entries(path),
				position   REAL    NOT NULL,
				hash       INTEGER
			);
			CREATE INDEX idx_p_hashes_entry ON p_hashes(entry_path);

			CREATE TABLE media_info (
				entry_path    TEXT PRIMARY KEY COLLATE NOCASE REFERENCES file_entries(path),
				duration_secs REAL NOT NULL
			);

			CREATE TABLE stream_info (
				media_path     TEXT NOT NULL COLLATE NOCASE REFERENCES media_info(entry_path),
				idx            TEXT,
				codec_name     TEXT,
				codec_long_name TEXT,
				codec_type     TEXT,
				pixel_format   TEXT,
				width          INTEGER,
				height         INTEGER,
				sample_rate    INTEGER,
				channel_layout TEXT,
				bit_rate       INTEGER,
				frame_rate     REAL,
				channels       INTEGER,
				hdr_format     TEXT
			);
			CREATE INDEX idx_stream_info_media ON stream_info(media_path);
		";
		cmd.Parameters.AddWithValue("$version", wrapper.Version);
		cmd.Parameters.AddWithValue("$pipeline", wrapper.ImageHashPipeline);
		cmd.ExecuteNonQuery();
	}

	static object ToBlob(uint[]? fingerprint) {
		if (fingerprint is null || fingerprint.Length == 0)
			return DBNull.Value;
		byte[] bytes = new byte[fingerprint.Length * sizeof(uint)];
		Buffer.BlockCopy(fingerprint, 0, bytes, 0, bytes.Length);
		return bytes;
	}
}
}



