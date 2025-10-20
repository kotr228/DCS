namespace GrayCat.Core.Models.BlockLibrary;
public class StatsBlock : BaseBlock
{
    public override string BlockType => "Stats";
    public override string DisplayName => "Statistics";
    public override string Description => "Display key metrics and numbers";

    public override Dictionary<string, object> DefaultProperties => new()
    {
        { "stats", "[{\"number\":\"1000+\",\"label\":\"Happy Clients\"},{\"number\":\"50+\",\"label\":\"Projects Completed\"},{\"number\":\"24/7\",\"label\":\"Support\"}]" },
        { "layout", "horizontal" }, // horizontal, vertical, grid
        { "animated", true }
    };

    public override string GenerateReactCode()
    {
        return @"
import React from 'react';
import './StatsBlock.css';

const StatsBlock = ({ stats, layout, animated }) => {
  const statsList = typeof stats === 'string' ? JSON.parse(stats) : stats;
  
  return (
    <div className={`stats stats-${layout} ${animated ? 'stats-animated' : ''}`}>
      {statsList.map((stat, i) => (
        <div key={i} className='stat-item'>
          <div className='stat-number'>{stat.number}</div>
          <div className='stat-label'>{stat.label}</div>
        </div>
      ))}
    </div>
  );
};

export default StatsBlock;";
    }
}
