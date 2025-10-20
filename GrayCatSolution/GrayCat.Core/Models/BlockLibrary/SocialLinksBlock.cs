namespace GrayCat.Core.Models.BlockLibrary;
public class SocialLinksBlock : BaseBlock
{
    public override string BlockType => "SocialLinks";
    public override string DisplayName => "Social Media Links";
    public override string Description => "Social media icon links";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "links", "[{\"platform\":\"facebook\",\"url\":\"#\"},{\"platform\":\"twitter\",\"url\":\"#\"},{\"platform\":\"instagram\",\"url\":\"#\"}]" },
        { "style", "icons" }, // icons, buttons, text
        { "size", "medium" }, // small, medium, large
        { "alignment", "center" }
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';
import './SocialLinksBlock.css';

const SocialLinksBlock = ({ links, style, size, alignment }) => {
  const linksList = typeof links === 'string' ? JSON.parse(links) : links;
  
  return (
    <div className={`social-links social-${style} social-${size} align-${alignment}`}>
      {linksList.map((link, i) => (
        <a 
          key={i} 
          href={link.url} 
          className={`social-link social-${link.platform}`}
          target='_blank'
          rel='noopener noreferrer'
          aria-label={link.platform}
        >
          <span className={`icon-${link.platform}`}></span>
        </a>
      ))}
    </div>
  );
};

export default SocialLinksBlock;";
    }
}
