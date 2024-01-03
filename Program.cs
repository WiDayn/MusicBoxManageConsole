using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;
using MySql.Data.MySqlClient;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

class Program
{
    public static string GetTraditional(string str)
    {
        string resultstr = string.Empty;
        resultstr = ChineseConverter.Convert(str, ChineseConversionDirection.SimplifiedToTraditional);
        return resultstr;
    }

    public static string GetSimplified(string str)
    {
        string resultstr = string.Empty;
        resultstr = ChineseConverter.Convert(str, ChineseConversionDirection.TraditionalToSimplified);
        return resultstr;
    }

    static void Main(string[] args)
    {
        Console.WriteLine("请输入音乐文件夹的路径:");
        var folderPath = Console.ReadLine();

        // 解析文件夹名
        var folderName = Path.GetFileName(folderPath);
        var parts = folderName.Split('-');
        if (parts.Length != 2) throw new FormatException("文件夹名格式不正确");

        var artistName = GetSimplified(parts[0].Trim());
        var albumTitle = parts[1].Trim();
        Console.WriteLine("请输入专辑发行时间（Eg.2023-01-01）:");
        var releaseDateString = Console.ReadLine();

        DateTime releaseDate;
        if (DateTime.TryParseExact(releaseDateString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out releaseDate))
        {
            Console.WriteLine($"Artist: {artistName}, Album: {albumTitle}, Release Date: {releaseDate.ToString("yyyy-MM-dd")}");
        }
        else
        {
            Console.WriteLine("发布日期格式不正确");
        }

        // 数据库连接
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();
        var connectionString = configuration["AppSettings:ConnString"];
        using var connection = new MySqlConnection(connectionString);
        connection.Open();

        // 检查并插入艺术家
        var artistId = GetOrInsertArtist(connection, artistName);

        // 检查并插入专辑
        var albumId = GetOrInsertAlbum(connection, albumTitle, artistId, releaseDate);

        // 处理每首歌曲
        var songFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(file => file.EndsWith(".mp3") || file.EndsWith(".m4a") || file.EndsWith(".flac"));

        foreach (var songFile in songFiles)
        {
            // 这里需要根据歌曲文件的元数据来获取歌曲信息
            // 例如歌曲名、时长、流派等
            var musicFile = TagLib.File.Create(songFile);

            var songTitle = Path.GetFileNameWithoutExtension(songFile);

            int trackNumber = (int)musicFile.Tag.Track;

            var artists = musicFile.Tag.Performers;

            // 插入歌曲并建立关系
            InsertSongAndRelationship(connection, songTitle, albumId, artists, musicFile.Tag.FirstGenre, trackNumber, musicFile.Properties.Duration, musicFile.Properties.AudioBitrate); // 添加其他必要参数
        }
    }

    static int GetOrInsertArtist(MySqlConnection connection, string artistName)
    {
        int artistId = 0;
        string queryCheck = "SELECT ArtistID FROM artists WHERE Name = @Name";
        MySqlCommand cmdCheck = new MySqlCommand(queryCheck, connection);
        cmdCheck.Parameters.AddWithValue("@Name", artistName);

        var result = cmdCheck.ExecuteScalar();
        if (result != null)
        {
            artistId = Convert.ToInt32(result);
        }
        else
        {
            string insertQuery = "INSERT INTO artists (Name) VALUES (@Name)";
            MySqlCommand cmdInsert = new MySqlCommand(insertQuery, connection);
            cmdInsert.Parameters.AddWithValue("@Name", artistName);

            cmdInsert.ExecuteNonQuery();

            artistId = (int)cmdInsert.LastInsertedId;
        }

        return artistId;
    }


    static int GetOrInsertAlbum(MySqlConnection connection, string albumTitle, int artistId, DateTime releaseDate)
    {
        int albumId = 0;
        string queryCheck = "SELECT AlbumID FROM albums WHERE Title = @Title AND ArtistID = @ArtistID";
        MySqlCommand cmdCheck = new MySqlCommand(queryCheck, connection);
        cmdCheck.Parameters.AddWithValue("@Title", albumTitle);
        cmdCheck.Parameters.AddWithValue("@ArtistID", artistId);

        var result = cmdCheck.ExecuteScalar();
        if (result != null)
        {
            albumId = Convert.ToInt32(result);
        }
        else
        {
            string insertQuery = "INSERT INTO albums (Title, ArtistID, ReleaseDate) VALUES (@Title, @ArtistID, @ReleaseDate)";
            MySqlCommand cmdInsert = new MySqlCommand(insertQuery, connection);
            cmdInsert.Parameters.AddWithValue("@Title", albumTitle);
            cmdInsert.Parameters.AddWithValue("@ArtistID", artistId);
            cmdInsert.Parameters.AddWithValue("@ReleaseDate", releaseDate);

            cmdInsert.ExecuteNonQuery();

            albumId = (int)cmdInsert.LastInsertedId;
        }

        return albumId;
    }

    static void InsertSongAndRelationship(MySqlConnection connection, string songTitle, int albumId, string[] artists, string genre, int trackNumber, TimeSpan duration, int bitrate)
    {
        string queryCheck = "SELECT SongID FROM songs WHERE Title = @Title AND AlbumID = @AlbumID";
        MySqlCommand cmdCheck = new MySqlCommand(queryCheck, connection);
        cmdCheck.Parameters.AddWithValue("@Title", songTitle);
        cmdCheck.Parameters.AddWithValue("@AlbumID", albumId);

        var result = cmdCheck.ExecuteScalar();
        if (result != null)
        {
            Console.WriteLine($"Title: {songTitle} AlbumID: {albumId} already exsit.");
            return;
        }

        string insertSongQuery = "INSERT INTO songs (Title, AlbumID, Genre, TrackNumber, Duration, Bitrate) VALUES (@Title, @AlbumID, @Genre, @TrackNumber, @Duration, @Bitrate)";
        MySqlCommand cmdInsertSong = new MySqlCommand(insertSongQuery, connection);
        cmdInsertSong.Parameters.AddWithValue("@Title", songTitle);
        cmdInsertSong.Parameters.AddWithValue("@AlbumID", albumId);
        cmdInsertSong.Parameters.AddWithValue("@Genre", genre);
        cmdInsertSong.Parameters.AddWithValue("@TrackNumber", trackNumber);
        cmdInsertSong.Parameters.AddWithValue("@Duration", (int)duration.TotalSeconds);
        cmdInsertSong.Parameters.AddWithValue("@Bitrate", bitrate);

        cmdInsertSong.ExecuteNonQuery();
        Console.WriteLine($"Title: {songTitle} AlbumID: {albumId} done.");

        int songId = (int)cmdInsertSong.LastInsertedId;

        foreach (string artist in artists)
        {
            int artistId = GetOrInsertArtist(connection, GetSimplified(artist));
            string insertRelationQuery = "INSERT INTO song_artists (SongID, ArtistID) VALUES (@SongID, @ArtistID)";
            MySqlCommand cmdInsertRelation = new MySqlCommand(insertRelationQuery, connection);
            cmdInsertRelation.Parameters.AddWithValue("@SongID", songId);
            cmdInsertRelation.Parameters.AddWithValue("@ArtistID", artistId);
            cmdInsertRelation.ExecuteNonQuery();
        }
    }
}
