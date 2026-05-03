using DishFinder.Interfaces;
using DishFinder.Models;
using Google.Cloud.PubSub.V1;
using System.Text.Json;

namespace DishFinder.Services
{
    public class PubSubPublisherService : IPubSubPublisherService
    {
        private readonly ILogger<PubSubPublisherService> _logger;
        private readonly string _projectId;
        private readonly string _topicId;

        public PubSubPublisherService(
            ILogger<PubSubPublisherService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _projectId = configuration["GoogleCloud:ProjectId"]
                ?? throw new InvalidOperationException("Missing GoogleCloud:ProjectId");

            _topicId = configuration["GoogleCloud:PubSubTopicId"]
                ?? throw new InvalidOperationException("Missing GoogleCloud:PubSubTopicId");
        }

        public async Task<string> PublishMenuUploadAsync(MenuUploadMessageModel message)
        {
            TopicName topicName = TopicName.FromProjectTopic(_projectId, _topicId);

            PublisherClient publisher = await new PublisherClientBuilder
            {
                TopicName = topicName
            }.BuildAsync();

            string payload = JsonSerializer.Serialize(message);
            string messageId = await publisher.PublishAsync(payload);

            _logger.LogInformation(
                "Published menu upload message. Topic={TopicId}, MessageId={MessageId}, MenuId={MenuId}, ImageId={ImageId}",
                _topicId,
                messageId,
                message.MenuId,
                message.ImageId);

            await publisher.ShutdownAsync(TimeSpan.FromSeconds(15));

            return messageId;
        }

    }
}
