namespace GrayCat.Core.Models.BlockLibrary;
public class PricingBlock : BaseBlock
{
    public override string BlockType => "Pricing";
    public override string DisplayName => "Pricing Card";
    public override string Description => "Pricing plan with features and CTA";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "planName", "Pro Plan" },
        { "price", "29" },
        { "currency", "$" },
        { "period", "month" },
        { "features", "[\"Feature 1\",\"Feature 2\",\"Feature 3\"]" },
        { "ctaText", "Get Started" },
        { "ctaUrl", "#" },
        { "highlighted", false },
        { "badge", "" }
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';
import './PricingBlock.css';

const PricingBlock = ({ planName, price, currency, period, features, ctaText, ctaUrl, highlighted, badge }) => {
  const featureList = typeof features === 'string' ? JSON.parse(features) : features;
  
  return (
    <div className={`pricing-card ${highlighted ? 'pricing-highlighted' : ''}`}>
      {badge && <span className='pricing-badge'>{badge}</span>}
      <h3 className='pricing-name'>{planName}</h3>
      <div className='pricing-price'>
        <span className='currency'>{currency}</span>
        <span className='amount'>{price}</span>
        <span className='period'>/{period}</span>
      </div>
      <ul className='pricing-features'>
        {featureList.map((feature, i) => (
          <li key={i}>{feature}</li>
        ))}
      </ul>
      <a href={ctaUrl} className='pricing-cta'>{ctaText}</a>
    </div>
  );
};

export default PricingBlock;";
    }
}
