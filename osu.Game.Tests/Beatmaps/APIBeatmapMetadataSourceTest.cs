// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Reflection;
using Moq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Tests.Beatmaps
{
    [TestFixture]
    public class APIBeatmapMetadataSourceTest
    {
        private readonly Mock<IAPIProvider> mockApi = new Mock<IAPIProvider>();
        private readonly Bindable<APIState> apiState = new Bindable<APIState>();
        public required APIBeatmapMetadataSource MetadataSource;

        [SetUp]
        public void SetUp()
        {
            mockApi.Setup(static api => api.State).Returns(apiState);
            MetadataSource = new APIBeatmapMetadataSource(mockApi.Object);
        }

        [Test]
        public void TestAvailableWhenAPIOnline()
        {
            apiState.Value = APIState.Online;

            Assert.That(MetadataSource.Available, Is.True);
        }

        [Test]
        public void TestNotAvailableWhenAPIOffline()
        {
            apiState.Value = APIState.Offline;

            Assert.That(MetadataSource.Available, Is.False);
        }

        [Test]
        public void TestTryLookupReturnsFalseWhenNotAvailable()
        {
            apiState.Value = APIState.Offline;

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.False);
            Assert.That(metadata, Is.Null);
        }

        [Test]
        public void TestTryLookupReturnsFalseOnException()
        {
            apiState.Value = APIState.Online;
            mockApi.Setup(static api => api.Perform(It.IsAny<GetBeatmapRequest>()))
                   .Throws(new Exception("Test exception"));

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.False);
            Assert.That(metadata, Is.Null);
        }

        [Test]
        public void TestTryLookupReturnsTrueOnFailedRequest()
        {
            apiState.Value = APIState.Online;
            mockApi.Setup(static api => api.Perform(It.IsAny<GetBeatmapRequest>()))
                   .Callback<APIRequest>(static req =>
                   {
                       if (req is GetBeatmapRequest getBeatmapReq)
                           setCompletionState(getBeatmapReq, APIRequestCompletionState.Failed);
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.True);
            Assert.That(metadata, Is.Null);
        }

        [Test]
        public void TestSuccessfulLookupWithoutTags()
        {
            var apiResponse = new APIBeatmap
            {
                OnlineID = 12345,
                OnlineBeatmapSetID = 67890,
                AuthorID = 1111,
                Status = BeatmapOnlineStatus.Ranked,
                LastUpdated = DateTimeOffset.Now,
                Checksum = "typeshit",
                BeatmapSet = new APIBeatmapSet
                {
                    Status = BeatmapOnlineStatus.Ranked,
                    Ranked = DateTimeOffset.Now.AddDays(-30),
                    Submitted = DateTimeOffset.Now.AddDays(-60)
                },
                TopTags = null
            };

            apiState.Value = APIState.Online;
            mockApi.Setup(static api => api.Perform(It.IsAny<GetBeatmapRequest>()))
                   .Callback<APIRequest>(req =>
                   {
                       var getBeatmapReq = req as GetBeatmapRequest;
                       setCompletionState(getBeatmapReq, APIRequestCompletionState.Completed);
                       setResponse(getBeatmapReq, apiResponse);
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.True);
            Assert.That(metadata, Is.Not.Null);
            Debug.Assert(metadata != null, nameof(metadata) + " != null");
            Assert.That(metadata.BeatmapID, Is.EqualTo(12345));
            Assert.That(metadata.BeatmapSetID, Is.EqualTo(67890));
            Assert.That(metadata.AuthorID, Is.EqualTo(1111));
            Assert.That(metadata.BeatmapStatus, Is.EqualTo(BeatmapOnlineStatus.Ranked));
            Assert.That(metadata.BeatmapSetStatus, Is.EqualTo(BeatmapOnlineStatus.Ranked));
            Assert.That(metadata.MD5Hash, Is.EqualTo("typeshit"));
            Assert.That(metadata.UserTags, Is.Empty);
        }

        [Test]
        public void TestPopulateUserTagsWithValidTags()
        {
            var topTags = new[]
            {
                new APIBeatmapTag { TagId = 1, VoteCount = 10 },
                new APIBeatmapTag { TagId = 2, VoteCount = 5 },
                new APIBeatmapTag { TagId = 3, VoteCount = 15 }
            };

            var relatedTags = new[]
            {
                new APITag { Id = 1, Name = "electronic" },
                new APITag { Id = 2, Name = "piano" },
                new APITag { Id = 3, Name = "dubstep" }
            };

            var apiResponse = new APIBeatmap
            {
                OnlineID = 12345,
                OnlineBeatmapSetID = 67890,
                AuthorID = 1111,
                Status = BeatmapOnlineStatus.Ranked,
                LastUpdated = DateTimeOffset.Now,
                Checksum = "typeshit",
                BeatmapSet = new APIBeatmapSet
                {
                    Status = BeatmapOnlineStatus.Ranked,
                    Ranked = DateTimeOffset.Now.AddDays(-30),
                    Submitted = DateTimeOffset.Now.AddDays(-60)
                },
                TopTags = topTags
            };

            var beatmapSetResponse = new APIBeatmapSet
            {
                RelatedTags = relatedTags
            };

            apiState.Value = APIState.Online;

            int performCallCount = 0;
            mockApi.Setup(static api => api.Perform(It.IsAny<APIRequest>()))
                   .Callback<APIRequest>(req =>
                   {
                       switch (req)
                       {
                           case GetBeatmapRequest getBeatmapReq:
                               setCompletionState(getBeatmapReq, APIRequestCompletionState.Completed);
                               setResponse(getBeatmapReq, apiResponse);
                               break;

                           case GetBeatmapSetRequest getBeatmapSetReq:
                               setCompletionState(getBeatmapSetReq, APIRequestCompletionState.Completed);
                               setResponse(getBeatmapSetReq, beatmapSetResponse);
                               break;
                       }

                       performCallCount++;
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.True);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(performCallCount, Is.EqualTo(2)); // One for GetBeatmapRequest, one for GetBeatmapSetRequest
            Debug.Assert(metadata != null, nameof(metadata) + " != null");
            Assert.That(metadata.UserTags, Has.Count.EqualTo(3));

            // Tags should be ordered by VoteCount descending, then by Name ascending
            string[] expectedOrder = new[] { "dubstep", "electronic", "piano" };
            Assert.That(metadata.UserTags.ToArray(), Is.EqualTo(expectedOrder));
        }

        [Test]
        public void TestPopulateUserTagsWithPartialMatchingTags()
        {
            var topTags = new[]
            {
                new APIBeatmapTag { TagId = 1, VoteCount = 10 },
                new APIBeatmapTag { TagId = 2, VoteCount = 5 },
                new APIBeatmapTag { TagId = 4, VoteCount = 15 } // This ID won't match any related tag
            };

            var relatedTags = new[]
            {
                new APITag { Id = 1, Name = "electronic" },
                new APITag { Id = 2, Name = "piano" },
                new APITag { Id = 3, Name = "dubstep" } // This tag won't match any top tag
            };

            var apiResponse = new APIBeatmap
            {
                OnlineID = 12345,
                OnlineBeatmapSetID = 67890,
                AuthorID = 1111,
                Status = BeatmapOnlineStatus.Ranked,
                LastUpdated = DateTimeOffset.Now,
                Checksum = "typeshit",
                BeatmapSet = new APIBeatmapSet
                {
                    Status = BeatmapOnlineStatus.Ranked,
                    Ranked = DateTimeOffset.Now.AddDays(-30),
                    Submitted = DateTimeOffset.Now.AddDays(-60)
                },
                TopTags = topTags
            };

            var beatmapSetResponse = new APIBeatmapSet
            {
                RelatedTags = relatedTags
            };

            apiState.Value = APIState.Online;
            mockApi.Setup(static api => api.Perform(It.IsAny<APIRequest>()))
                   .Callback<APIRequest>(req =>
                   {
                       switch (req)
                       {
                           case GetBeatmapRequest getBeatmapReq:
                               setCompletionState(getBeatmapReq, APIRequestCompletionState.Completed);
                               setResponse(getBeatmapReq, apiResponse);
                               break;

                           case GetBeatmapSetRequest getBeatmapSetReq:
                               setCompletionState(getBeatmapSetReq, APIRequestCompletionState.Completed);
                               setResponse(getBeatmapSetReq, beatmapSetResponse);
                               break;
                       }
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.True);
            Assert.That(metadata, Is.Not.Null);
            Debug.Assert(metadata != null, nameof(metadata) + " != null");
            Assert.That(metadata.UserTags, Has.Count.EqualTo(2)); // Only matching tags

            string[] expectedOrder = new[] { "electronic", "piano" };
            Assert.That(metadata.UserTags.ToArray(), Is.EqualTo(expectedOrder));
        }

        [Test]
        public void TestPopulateUserTagsExceptionHandling()
        {
            var apiResponse = new APIBeatmap
            {
                OnlineID = 12345,
                OnlineBeatmapSetID = 67890,
                AuthorID = 1111,
                Status = BeatmapOnlineStatus.Ranked,
                LastUpdated = DateTimeOffset.Now,
                Checksum = "typeshit",
                BeatmapSet = new APIBeatmapSet
                {
                    Status = BeatmapOnlineStatus.Ranked,
                    Ranked = DateTimeOffset.Now.AddDays(-30),
                    Submitted = DateTimeOffset.Now.AddDays(-60)
                },
                TopTags = new[] { new APIBeatmapTag { TagId = 1, VoteCount = 10 } }
            };

            apiState.Value = APIState.Online;

            mockApi.Setup(static api => api.Perform(It.IsAny<APIRequest>()))
                   .Callback<APIRequest>(req =>
                   {
                       switch (req)
                       {
                           case GetBeatmapRequest getBeatmapReq:
                               setCompletionState(getBeatmapReq, APIRequestCompletionState.Completed);
                               setResponse(getBeatmapReq, apiResponse);
                               break;

                           case GetBeatmapSetRequest:
                               throw new Exception("Test exception during tag retrieval");
                       }
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.True);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata?.UserTags, Is.Empty);
        }

        [Test]
        public void TestPopulateUserTagsWithEmptyTopTags()
        {
            var apiResponse = new APIBeatmap
            {
                OnlineID = 12345,
                OnlineBeatmapSetID = 67890,
                AuthorID = 1111,
                Status = BeatmapOnlineStatus.Ranked,
                LastUpdated = DateTimeOffset.Now,
                Checksum = "typeshit",
                BeatmapSet = new APIBeatmapSet
                {
                    Status = BeatmapOnlineStatus.Ranked
                },
                TopTags = Array.Empty<APIBeatmapTag>()
            };

            apiState.Value = APIState.Online;

            // Reset the mock to clear any previous setups/calls from SetUp
            mockApi.Reset();
            mockApi.Setup(static api => api.State).Returns(apiState);

            mockApi.Setup(static api => api.Perform(It.IsAny<GetBeatmapRequest>()))
                   .Callback<APIRequest>(req =>
                   {
                       var getBeatmapReq = req as GetBeatmapRequest;

                       setCompletionState(getBeatmapReq, APIRequestCompletionState.Completed);
                       setResponse(getBeatmapReq, apiResponse);
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.True);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata?.UserTags, Is.Empty);

            // Verify that only one API call was made (no call to GetBeatmapSetRequest)
            mockApi.Verify(static api => api.Perform(It.IsAny<GetBeatmapRequest>()), Times.Once);
            mockApi.Verify(static api => api.Perform(It.IsAny<GetBeatmapSetRequest>()), Times.Never);
        }

        [Test]
        public void TestPopulateUserTagsWithFailedBeatmapSetRequest()
        {
            var apiResponse = new APIBeatmap
            {
                OnlineID = 12345,
                OnlineBeatmapSetID = 67890,
                AuthorID = 1111,
                Status = BeatmapOnlineStatus.Ranked,
                LastUpdated = DateTimeOffset.Now,
                Checksum = "typeshit",
                BeatmapSet = new APIBeatmapSet
                {
                    Status = BeatmapOnlineStatus.Ranked
                },
                TopTags = new[] { new APIBeatmapTag { TagId = 1, VoteCount = 10 } }
            };

            apiState.Value = APIState.Online;
            mockApi.Setup(static api => api.Perform(It.IsAny<APIRequest>()))
                   .Callback<APIRequest>(req =>
                   {
                       switch (req)
                       {
                           case GetBeatmapRequest getBeatmapReq:
                               setCompletionState(getBeatmapReq, APIRequestCompletionState.Completed);
                               setResponse(getBeatmapReq, apiResponse);
                               break;

                           case GetBeatmapSetRequest getBeatmapSetReq:
                               setCompletionState(getBeatmapSetReq, APIRequestCompletionState.Failed);
                               break;
                       }
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.True);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata?.UserTags, Is.Empty); // Tags should be empty due to failed request
        }

        [Test]
        public void TestTryLookupWithNullMD5Hash()
        {
            apiState.Value = APIState.Online;

            // Reset the mock to clear any previous setups/calls from SetUp
            mockApi.Reset();
            mockApi.Setup(static api => api.State).Returns(apiState);

            mockApi.Setup(static api => api.Perform(It.IsAny<GetBeatmapRequest>()))
                   .Callback<APIRequest>(static req =>
                   {
                       var getBeatmapReq = req as GetBeatmapRequest;
                       // When MD5Hash is null, the API should return null response
                       setCompletionState(getBeatmapReq, APIRequestCompletionState.Completed);
                       setResponse(getBeatmapReq, null);
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = null,
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.False);
            Assert.That(metadata, Is.Null);
            mockApi.Verify(static api => api.Perform(It.IsAny<GetBeatmapRequest>()), Times.Once);
        }

        [Test]
        public void TestTryLookupWithNullResponse()
        {
            apiState.Value = APIState.Online;
            mockApi.Setup(static api => api.Perform(It.IsAny<GetBeatmapRequest>()))
                   .Callback<APIRequest>(static req =>
                   {
                       var getBeatmapReq = req as GetBeatmapRequest;
                       setCompletionState(getBeatmapReq, APIRequestCompletionState.Completed);
                       setResponse(getBeatmapReq, null);
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.False);
            Assert.That(metadata, Is.Null);
        }

        [Test]
        public void TestPopulateUserTagsWithNullRelatedTags()
        {
            var apiResponse = new APIBeatmap
            {
                OnlineID = 12345,
                OnlineBeatmapSetID = 67890,
                AuthorID = 1111,
                Status = BeatmapOnlineStatus.Ranked,
                LastUpdated = DateTimeOffset.Now,
                Checksum = "typeshit",
                BeatmapSet = new APIBeatmapSet
                {
                    Status = BeatmapOnlineStatus.Ranked,
                    Ranked = DateTimeOffset.Now.AddDays(-30),
                    Submitted = DateTimeOffset.Now.AddDays(-60)
                },
                TopTags = new[] { new APIBeatmapTag { TagId = 1, VoteCount = 10 } }
            };

            var beatmapSetResponse = new APIBeatmapSet
            {
                RelatedTags = null
            };

            apiState.Value = APIState.Online;
            mockApi.Setup(static api => api.Perform(It.IsAny<APIRequest>()))
                   .Callback<APIRequest>(req =>
                   {
                       switch (req)
                       {
                           case GetBeatmapRequest getBeatmapReq:
                               setCompletionState(getBeatmapReq, APIRequestCompletionState.Completed);
                               setResponse(getBeatmapReq, apiResponse);
                               break;

                           case GetBeatmapSetRequest getBeatmapSetReq:
                               setCompletionState(getBeatmapSetReq, APIRequestCompletionState.Completed);
                               setResponse(getBeatmapSetReq, beatmapSetResponse);
                               break;
                       }
                   });

            var beatmapInfo = new BeatmapInfo
            {
                MD5Hash = "test",
                BeatmapSet = new BeatmapSetInfo()
            };

            bool result = MetadataSource.TryLookup(beatmapInfo, out var metadata);

            Assert.That(result, Is.True);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata?.UserTags, Is.Empty);
        }

        [Test]
        public void TestDispose()
        {
            Assert.DoesNotThrow(() => MetadataSource.Dispose());
        }

        private static void setCompletionState(APIRequest? request, APIRequestCompletionState state)
        {
            if (request == null) return;

            var field = typeof(APIRequest).GetField("<CompletionState>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(request, state);
        }

        private static void setResponse<T>(APIRequest<T>? request, T? response) where T : class
        {
            if (request == null) return;

            var field = typeof(APIRequest<T>).GetField("<Response>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(request, response);
        }
    }
}
