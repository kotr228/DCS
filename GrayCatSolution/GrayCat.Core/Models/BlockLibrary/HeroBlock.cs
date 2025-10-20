namespace GrayCat.Core.Models.BlockLibrary;

public class HeroBlock : BaseBlock
{
    public override string BlockType => "Hero";
    public override string DisplayName => "Hero Section";
    public override string Description => "Large promotional section with headline, subtext, and CTA";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "headline", "Welcome to Our Site" },
        { "subheadline", "Build amazing things together" },
        { "ctaText", "Get Started" },
        { "ctaUrl", "#" },
        { "backgroundImage", "" },
        { "overlay", true },
        { "overlayOpacity", 0.5 },
        { "alignment", "center" }, // left, center, right
        { "height", "medium" } // small, medium, large, full
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';
import './HeroBlock.css';

const HeroBlock = ({
  headline,
  subheadline,
  ctaText,
  ctaUrl,
  backgroundImage,
  overlay,
  overlayOpacity,
  alignment,
  height
}) => {
  const style = {
    backgroundImage: backgroundImage ? `url(${backgroundImage})` : undefined,
    textAlign: alignment
  };
  
  return (
    <section className={`hero hero-${height}`} style={style}>
      {overlay && <div className='hero-overlay' style={{ opacity: overlayOpacity }}></div>}
      <div className='hero-content'>
        <h1 className='hero-headline'>{headline}</h1>
        <p className='hero-subheadline'>{subheadline}</p>
        {ctaText && (
          <a href={ctaUrl} className='hero-cta'>
            {ctaText}
          </a>
        )}
      </div>
    </section>
  );
};

export default HeroBlock;";
    }
}