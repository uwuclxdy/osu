// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;

namespace osu.Game.Beatmaps
{
    /// <summary>
    /// Performs online metadata lookups using the osu-web API.
    /// </summary>
    public class APIBeatmapMetadataSource : IOnlineBeatmapMetadataSource
    {
        private readonly IAPIProvider api;

        public APIBeatmapMetadataSource(IAPIProvider api)
        {
            this.api = api;
        }

        public bool Available => api.State.Value == APIState.Online;

        public bool TryLookup(BeatmapInfo beatmapInfo, out OnlineBeatmapMetadata? onlineMetadata)
        {
            if (!Available)
            {
                onlineMetadata = null;
                return false;
            }

            Debug.Assert(beatmapInfo.BeatmapSet != null);

            var req = new GetBeatmapRequest(md5Hash: beatmapInfo.MD5Hash, filename: beatmapInfo.Path);

            try
            {
                // intentionally blocking to limit web request concurrency
                api.Perform(req);

                if (req.CompletionState == APIRequestCompletionState.Failed)
                {
                    logForModel(beatmapInfo.BeatmapSet, $@"Online retrieval failed for {beatmapInfo}");
                    onlineMetadata = null;
                    return true;
                }

                var res = req.Response;

                if (res != null)
                {
                    logForModel(beatmapInfo.BeatmapSet, $@"Online retrieval mapped {beatmapInfo} to {res.OnlineBeatmapSetID} / {res.OnlineID}.");

                    onlineMetadata = new OnlineBeatmapMetadata
                    {
                        BeatmapID = res.OnlineID,
                        BeatmapSetID = res.OnlineBeatmapSetID,
                        AuthorID = res.AuthorID,
                        BeatmapStatus = res.Status,
                        BeatmapSetStatus = res.BeatmapSet?.Status,
                        DateRanked = res.BeatmapSet?.Ranked,
                        DateSubmitted = res.BeatmapSet?.Submitted,
                        MD5Hash = res.MD5Hash,
                        LastUpdated = res.LastUpdated,
                    };

                    try
                    {
                        populateUserTags(onlineMetadata, res); // get user tags with a second web request
                    }
                    catch (Exception tagException)
                    {
                        logForModel(beatmapInfo.BeatmapSet, $@"Failed to populate user tags for {beatmapInfo} ({tagException.Message})");
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                logForModel(beatmapInfo.BeatmapSet, $@"Online retrieval failed for {beatmapInfo} ({e.Message})");
                onlineMetadata = null;
                return false;
            }

            onlineMetadata = null;
            return false;
        }

        private void logForModel(BeatmapSetInfo set, string message) =>
            RealmArchiveModelImporter<BeatmapSetInfo>.LogForModel(set, $@"[{nameof(APIBeatmapMetadataSource)}] {message}");

        private void populateUserTags(OnlineBeatmapMetadata onlineMetadata, Online.API.Requests.Responses.APIBeatmap beatmap)
        {
            if (beatmap.TopTags == null || beatmap.TopTags.Length == 0)
                return;

            var setBeatmapSetRequest = new GetBeatmapSetRequest(beatmap.OnlineBeatmapSetID);

            api.Perform(setBeatmapSetRequest);

            if (setBeatmapSetRequest.CompletionState != APIRequestCompletionState.Completed || setBeatmapSetRequest.Response?.RelatedTags == null)
                return;

            var tagsById = setBeatmapSetRequest.Response.RelatedTags.ToDictionary(static t => t.Id);

            string[] userTags = beatmap.TopTags
                                       .Where(t => tagsById.TryGetValue(t.TagId, out _))
                                       .Select(t => (topTag: t, relatedTag: tagsById[t.TagId]))
                                       .OrderByDescending(static t => t.topTag.VoteCount)
                                       .ThenBy(static t => t.relatedTag.Name)
                                       .Select(static t => t.relatedTag.Name)
                                       .ToArray();

            onlineMetadata.UserTags.AddRange(userTags);
        }

        public void Dispose()
        {
        }
    }
}
