namespace GrayCat.Core.Models.BlockLibrary;
public class VideoBlock : BaseBlock
{
    public override string BlockType => "Video";
    public override string DisplayName => "Video Player";
    public override string Description => "Embedded video player with controls";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "videoUrl", "" },
        { "provider", "youtube" }, // youtube, vimeo, custom
        { "autoplay", false },
        { "controls", true },
        { "loop", false },
        { "muted", false },
        { "aspectRatio", "16:9" } // 16:9, 4:3, 1:1
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';
import './VideoBlock.css';

const VideoBlock = ({ videoUrl, provider, autoplay, controls, loop, muted, aspectRatio }) => {
  const getEmbedUrl = () => {
    if (provider === 'youtube') {
      const videoId = videoUrl.split('v=')[1]?.split('&')[0];
      return `https://www.youtube.com/embed/${videoId}?autoplay=${autoplay ? 1 : 0}`;
    }
    return videoUrl;
  };
  
  return (
    <div className={`video-block video-${aspectRatio.replace(':', '-')}`}>
      <iframe
        src={getEmbedUrl()}
        allow='accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture'
        allowFullScreen
      />
    </div>
  );
};

export default VideoBlock;";
    }
}
