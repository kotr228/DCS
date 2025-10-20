namespace GrayCat.Core.Models.BlockLibrary;
public class AccordionBlock : BaseBlock
{
    public override string BlockType => "Accordion";
    public override string DisplayName => "Accordion/FAQ";
    public override string Description => "Collapsible content sections";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "title", "Frequently Asked Questions" },
        { "items", "[{\"question\":\"What is this?\",\"answer\":\"This is an answer.\"},{\"question\":\"How does it work?\",\"answer\":\"It works like this.\"}]" },
        { "allowMultiple", false },
        { "defaultOpen", 0 }
    };

    public override string GenerateReactCode()
    {
        return @"
import React, { useState } from 'react';
import './AccordionBlock.css';

const AccordionBlock = ({ title, items, allowMultiple, defaultOpen }) => {
  const itemsList = typeof items === 'string' ? JSON.parse(items) : items;
  const [openIndices, setOpenIndices] = useState(new Set([defaultOpen]));
  
  const toggleItem = (index) => {
    const newOpen = new Set(openIndices);
    if (newOpen.has(index)) {
      newOpen.delete(index);
    } else {
      if (!allowMultiple) newOpen.clear();
      newOpen.add(index);
    }
    setOpenIndices(newOpen);
  };
  
  return (
    <div className='accordion'>
      {title && <h2 className='accordion-title'>{title}</h2>}
      {itemsList.map((item, i) => (
        <div key={i} className={`accordion-item ${openIndices.has(i) ? 'open' : ''}`}>
          <button 
            className='accordion-header' 
            onClick={() => toggleItem(i)}
          >
            <span>{item.question}</span>
            <span className='accordion-icon'>{openIndices.has(i) ? '−' : '+'}</span>
          </button>
          <div className='accordion-content'>
            <p>{item.answer}</p>
          </div>
        </div>
      ))}
    </div>
  );
};

export default AccordionBlock;";
    }
}
