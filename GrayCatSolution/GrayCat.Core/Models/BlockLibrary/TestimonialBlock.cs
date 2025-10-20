namespace GrayCat.Core.Models.BlockLibrary;
public class TestimonialBlock : BaseBlock
{
    public override string BlockType => "Testimonial";
    public override string DisplayName => "Testimonial";
    public override string Description => "Customer testimonial with quote and author";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "quote", "This product changed my life!" },
        { "author", "John Doe" },
        { "role", "CEO, Company" },
        { "avatar", "" },
        { "rating", 5 },
        { "showRating", true },
        { "layout", "default" } // default, card, minimal
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';
import './TestimonialBlock.css';

const TestimonialBlock = ({ quote, author, role, avatar, rating, showRating, layout }) => {
  return (
    <div className={`testimonial testimonial-${layout}`}>
      <div className='testimonial-quote'>
        <span className='quote-mark'>&quot;</span>
        <p>{quote}</p>
      </div>
      {showRating && (
        <div className='testimonial-rating'>
          {'★'.repeat(rating)}{'☆'.repeat(5 - rating)}
        </div>
      )}
      <div className='testimonial-author'>
        {avatar && <img src={avatar} alt={author} className='testimonial-avatar' />}
        <div>
          <div className='author-name'>{author}</div>
          <div className='author-role'>{role}</div>
        </div>
      </div>
    </div>
  );
};

export default TestimonialBlock;";
    }
}
