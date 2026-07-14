using DishFinder.Models;

namespace DishFinder.Interfaces
{
    public interface IPubSubPublisherService
    {
        Task<string> PublishMenuUploadAsync(MenuUploadMessageModel message);
    }
}
