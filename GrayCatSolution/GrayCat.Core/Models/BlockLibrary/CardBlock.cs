namespace GrayCat.Core.Models.BlockLibrary;

public class CardBlock : BaseBlock
{
    public override string BlockType => "Card";
    public override string DisplayName => "Card Block";
    public override string Description => "Content card with image, title, and description";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "title", "Card Title" },
        { "description", "Card description text goes here" },
        { "image", "" },
        { "imagePosition", "top" }, // top, left, right, bottom
        { "linkText", "Learn More" },
        { "linkUrl", "#" },
        { "shadow", true },
        { "bordered", false },
        { "hoverable", true }
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';
import './CardBlock.css';

const CardBlock = ({
  title,
  description,
  image,
  imagePosition,
  linkText,
  linkUrl,
  shadow,
  bordered,
  hoverable
}) => {
  const className = `card card-${imagePosition} ${shadow ? 'card-shadow' : ''} ${bordered ? 'card-bordered' : ''} ${hoverable ? 'card-hover' : ''}`;
  
  return (
    <div className={className}>
      {image && (
        <div className='card-image'>
          <img src={image} alt={title} />
        </div>
      )}
      <div className='card-body'>
        <h3 className='card-title'>{title}</h3>
        <p className='card-description'>{description}</p>
        {linkText && (
          <a href={linkUrl} className='card-link'>
            {linkText}
          </a>
        )}
      </div>
    </div>
  );
};

export default CardBlock;";
    }
}