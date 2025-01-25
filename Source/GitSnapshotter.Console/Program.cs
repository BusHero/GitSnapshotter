using System.Text.Json;

using GitSnapshotter;

var snapshot = GitRepository.GetSnapshot(args[0]);

var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

Console.WriteLine(json);
